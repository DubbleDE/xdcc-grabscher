//  
//  Copyright (C) 2010 Lars Formella <ich@larsformella.de>
// 
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
// 
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
// 

using System;
using System.Collections.Generic;
using MySql.Data.MySqlClient;
using XG.Core;

namespace XG.Server.Backend.MySql
{
	public class MySqlBackend : IServerPlugin
	{
		#region VARIABLES

		private ServerRunner myRunner;
		private MySqlConnection myDbConnection;

		private object locked = new object();

		#endregion

		#region RUN STOP RESTART
		
		public void Start (ServerRunner aParent)
		{
			this.myRunner = aParent;
			this.myRunner.ObjectAddedEvent += new ObjectObjectDelegate(myRunner_ObjectAddedEventHandler);
			this.myRunner.ObjectChangedEvent += new ObjectDelegate(myRunner_ObjectChangedEventHandler);
			this.myRunner.ObjectRemovedEvent += new ObjectObjectDelegate(myRunner_ObjectRemovedEventHandler);

			string connectionString = "Server=localhost;Database=xg;User ID=xg;Password=xg;Pooling=false";
			this.myDbConnection = new MySqlConnection(connectionString);
			this.myDbConnection.Open();
		}
		
		
		public void Stop ()
		{
			this.myRunner.ObjectAddedEvent -= new ObjectObjectDelegate(myRunner_ObjectAddedEventHandler);
			this.myRunner.ObjectChangedEvent -= new ObjectDelegate(myRunner_ObjectChangedEventHandler);
			this.myRunner.ObjectRemovedEvent -= new ObjectObjectDelegate(myRunner_ObjectRemovedEventHandler);
		}
		
		
		public void Restart ()
		{
			this.Stop();
			this.Start(this.myRunner);
		}
		
		#endregion

		#region EVENTS

		protected void myRunner_ObjectAddedEventHandler(XGObject aParentObj, XGObject aObj)
		{
			string table = "";
			Dictionary<string, object> dic = this.GetObjectData(aObj);

			if (aObj.GetType() == typeof(XGServer)) { table = "server"; }
			else if (aObj.GetType() == typeof(XGChannel)) { table = "channel"; }
			else if (aObj.GetType() == typeof(XGBot)) { table = "bot"; }
			else if (aObj.GetType() == typeof(XGPacket)) { table = "packet"; }

			if(table != "")
			{
				dic.Add("guid", aObj.Guid.ToString());

				string values1 = "";
				string values2 = "";
				foreach(KeyValuePair<string, object> kcp in dic)
				{
					if(values1 != "")
					{
						values1 += ", ";
						values2 += ", ";
					}
					values1 += kcp.Key;
					values2 += "@" + kcp.Key;
				}

				this.ExecuteQuery("INSERT INTO " + table +" (" + values1 + ") VALUES (" + values2 + ")", dic);
			}
		}

		protected void myRunner_ObjectChangedEventHandler(XGObject aObj)
		{
			string table = "";
			Dictionary<string, object> dic = this.GetObjectData(aObj);

			if (aObj.GetType() == typeof(XGServer)) { table = "server"; }
			else if (aObj.GetType() == typeof(XGChannel)) { table = "channel"; }
			else if (aObj.GetType() == typeof(XGBot)) { table = "bot"; }
			else if (aObj.GetType() == typeof(XGPacket)) { table = "packet"; }

			if(table != "")
			{
				string values1 = "";
				foreach(KeyValuePair<string, object> kcp in dic)
				{
					if(values1 != "")
					{
						values1 += ", ";
					}
					values1 += kcp.Key + " = @" + kcp.Key;
				}

				dic.Add("guid", aObj.Guid.ToString());
				this.ExecuteQuery("UPDATE " + table +" SET " + values1 + " WHERE Guid = @guid", dic);
			}
		}

		protected void myRunner_ObjectRemovedEventHandler(XGObject aParentObj, XGObject aObj)
		{
			string table = "";
			Dictionary<string, object> dic = new Dictionary<string, object>();

			if (aObj.GetType() == typeof(XGServer)) { table = "server"; }
			else if (aObj.GetType() == typeof(XGChannel)) { table = "channel"; }
			else if (aObj.GetType() == typeof(XGBot)) { table = "bot"; }
			else if (aObj.GetType() == typeof(XGPacket)) { table = "packet"; }

			if(table != "")
			{
				dic.Add("guid", aObj.Guid.ToString());
				this.ExecuteQuery("DELETE FROM " + table +" WHERE Guid = @guid", dic);
			}
		}

		#endregion

		#region HELPER

		protected Dictionary<string, object> GetObjectData(XGObject aObj)
		{
			Dictionary<string, object> dic = new Dictionary<string, object>();
			dic.Add("Name", aObj.Name);
			dic.Add("Connected", aObj.Connected);
			dic.Add("Enabled", aObj.Enabled);
			dic.Add("LastModified", this.Date2Timestamp(aObj.LastModified));

			if (aObj.GetType() == typeof(XGServer))
			{
				XGServer obj = (XGServer)aObj;
				dic.Add("Port", obj.Port);
			}
			else if (aObj.GetType() == typeof(XGChannel))
			{
				XGChannel obj = (XGChannel)aObj;
				dic.Add("ParentGuid", obj.ParentGuid);
			}
			else if (aObj.GetType() == typeof(XGBot))
			{
				XGBot obj = (XGBot)aObj;
				dic.Add("ParentGuid", obj.ParentGuid);
				dic.Add("BotState", obj.BotState);
				dic.Add("InfoQueueCurrent", obj.InfoQueueCurrent);
				dic.Add("InfoQueueTotal", obj.InfoQueueTotal);
				dic.Add("InfoSlotCurrent", obj.InfoSlotCurrent);
				dic.Add("InfoSlotTotal", obj.InfoSlotTotal);
				dic.Add("InfoSpeedCurrent", obj.InfoSpeedCurrent);
				dic.Add("InfoSpeedMax", obj.InfoSpeedMax);
				dic.Add("LastContact", this.Date2Timestamp(obj.LastContact));
				dic.Add("LastMessage", obj.LastMessage);
			}
			else if (aObj.GetType() == typeof(XGPacket))
			{
				XGPacket obj = (XGPacket)aObj;
				dic.Add("ParentGuid", obj.ParentGuid);
				dic.Add("Id", obj.Id);
				dic.Add("LastUpdated", this.Date2Timestamp(obj.LastUpdated));
				dic.Add("Size", obj.Size);
			}

			return dic;
		}

		protected void ExecuteQuery(string aSql, Dictionary<string, object> aDic)
		{
			lock(locked)
			{
				MySqlCommand cmd = new MySqlCommand(aSql, this.myDbConnection);
				foreach(KeyValuePair<string, object> kcp in aDic)
				{
					cmd.Parameters.AddWithValue("@" + kcp.Key, kcp.Value);
				}
				try
				{
					cmd.ExecuteNonQuery();
				}
				catch (Exception ex)
				{
					this.Log("ExecuteQuery() '" + aSql + "' : " + XGHelper.GetExceptionMessage(ex), LogLevel.Exception);
				}
			}
		}

        protected Int64 Date2Timestamp(DateTime aDate)
        {
            DateTime date = new DateTime(1970, 1, 1);
            TimeSpan ts = new TimeSpan(aDate.Ticks - date.Ticks);
            return (Convert.ToInt64(ts.TotalSeconds));
        }

		#endregion

		#region LOG

		/// <summary>
		/// Calls XGGelper.Log()
		/// </summary>
		/// <param name="aData"></param>
		/// <param name="aLevel"></param>
		private void Log(string aData, LogLevel aLevel)
		{
			XGHelper.Log("MySqlBackend." + aData, aLevel);
		}

		#endregion
	}
}
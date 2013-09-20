//
//  Server.cs
//  This file is part of XG - XDCC Grabscher
//  http://www.larsformella.de/lang/en/portfolio/programme-software/xg
//
//  Author:
//       Lars Formella <ich@larsformella.de>
//
//  Copyright (c) 2012 Lars Formella
//
//  This program is free software; you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation; either version 2 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
//

using Fleck;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;

using XG.Core;
using XG.Server.Plugin.General.Webserver.Object;
using XG.Server.Worker;

using log4net;
using SharpRobin.Core;

namespace XG.Server.Plugin.General.Webserver.Websocket
{
	public class Server : ASaltedPassword
	{
		#region VARIABLES

		static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		WebSocketServer _webSocket;
		JsonSerializerSettings _jsonSerializerSettings;

		readonly HashSet<User> _users = new HashSet<User>();
		
		static readonly Core.Search _searchEnabled = new Core.Search { Guid = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "Enabled Packets" };
		static readonly Core.Search _searchDownloads = new Core.Search { Guid = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = "Downloads" };

		public RrdDb RrdDb { get; set; }

		#endregion

		public Server()
		{
			_jsonSerializerSettings = new JsonSerializerSettings
			{
				DateFormatHandling = DateFormatHandling.MicrosoftDateFormat,
				DateParseHandling = DateParseHandling.DateTime,
				DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind
			};
			_jsonSerializerSettings.Converters.Add(new DoubleConverter());
		}

		#region AWorker

		protected override void StartRun ()
		{
			_webSocket = new WebSocketServer("ws://localhost:" + (Settings.Instance.WebServerPort + 1));
			//FleckLog.Level = LogLevel.Debug;

			_webSocket.Start (socket =>
			{
				socket.OnOpen = () => OnOpen(socket);
				socket.OnClose = () => OnClose(socket);
				socket.OnMessage = message => OnMessage(socket, message);
				socket.OnError = exception => OnError(socket, exception);
			});
		}

		protected override void StopRun()
		{
			//_webSocket.Stop();
		}

		#endregion

		#region REPOSITORY EVENTS

		protected override void ObjectAdded(Core.AObject aParent, Core.AObject aObj)
		{
			Broadcast(Response.Types.ObjectAdded, aObj, true);
		}

		protected override void ObjectRemoved(Core.AObject aParent, Core.AObject aObj)
		{
			Broadcast(Response.Types.ObjectRemoved, aObj, true);
		}

		protected override void ObjectChanged(Core.AObject aObj, string[] aFields)
		{
			Broadcast(Response.Types.ObjectChanged, aObj, true);

			HashSet<string> fields = new HashSet<string>(aFields);

			// if a bot changed dispatch the packets, too
			if (aObj is Core.Bot)
			{
				if (fields.Contains("Connected"))
				{
					foreach (var pack in (aObj as Core.Bot).Packets)
					{
						Broadcast(Response.Types.ObjectChanged, pack, true);
					}
				}
			}
			// if a part changed dispatch the file, packet and bot, too
			else if (aObj is FilePart)
			{
				var part = aObj as FilePart;
				Broadcast(Response.Types.ObjectChanged, part.Parent, false);

				if (part.Packet != null)
				{
					if (fields.Contains("Speed") || fields.Contains("CurrentSize") || fields.Contains("TimeMissing"))
					{
						Broadcast(Response.Types.ObjectChanged, part.Packet, false);
					}
					if (fields.Contains("Speed"))
					{
						Broadcast(Response.Types.ObjectChanged, part.Packet.Parent, false);
					}
				}
			}
		}

		protected override void ObjectEnabledChanged(Core.AObject aObj)
		{
			Broadcast(Response.Types.ObjectChanged, aObj, false);

			// if a packet changed dispatch the bot, too
			if (aObj is Core.Packet)
			{
				var part = aObj as Core.Packet;
				Broadcast(Response.Types.ObjectChanged, part.Parent, false);
			}
		}

		protected override void FileAdded(Core.AObject aParent, Core.AObject aObj)
		{
			ObjectAdded(aParent, aObj);
		}

		protected override void FileRemoved(Core.AObject aParent, Core.AObject aObj)
		{
			ObjectRemoved(aParent, aObj);
		}

		protected override void FileChanged(Core.AObject aObj, string[] aFields)
		{
			ObjectChanged(aObj, aFields);
		}

		protected override void SearchAdded(Core.AObject aParent, Core.AObject aObj)
		{
			ObjectAdded(aParent, aObj);
		}

		protected override void SearchRemoved(Core.AObject aParent, Core.AObject aObj)
		{
			ObjectRemoved(aParent, aObj);
		}

		protected override void SearchChanged(Core.AObject aObj, string[] aFields)
		{
			ObjectChanged(aObj, aFields);
		}

		protected override void NotificationAdded(Core.AObject aParent, Core.AObject aObj)
		{
			ObjectAdded(aParent, aObj);
		}

		#endregion

		#region WebSocket

		void OnOpen(IWebSocketConnection aContext)
		{
			Log.Info("OnOpen(" + aContext.ConnectionInfo.ClientIpAddress + ")");

			var user = new User
			{
				Connection = aContext,
				LoadedObjects = new HashSet<Guid>(),
				LastSearchRequest = null
			};

			_users.Add(user);
		}

		void OnClose(IWebSocketConnection aContext)
		{
			Log.Info("OnClose(" + aContext.ConnectionInfo.ClientIpAddress + ")");

			foreach (var user in _users.ToArray())
			{
				if (user.Connection == aContext)
				{
					_users.Remove(user);
				}
			}
		}

		void OnMessage(IWebSocketConnection aContext, string aMessage)
		{
			Log.Info("OnMessage(" + aContext.ConnectionInfo.ClientIpAddress + ", " + aMessage + ")");

			var currentUser = (from user in _users where user.Connection == aContext select user).SingleOrDefault();
			var request = JsonConvert.DeserializeObject<Request>(aMessage);
#if !UNSAFE
			try
			{
#endif
				// no pass, no way
				if (request.Password != Password)
				{
					Log.Error("OnMessage(" + aContext.ConnectionInfo.ClientIpAddress + ") bad password");
					// exit
					return;
				}

				switch (request.Type)
				{
					case Request.Types.AddServer:
						OnAddServer(request.Name);
						break;

					case Request.Types.RemoveServer:
						RemoveServer(request.Guid);
						break;

					case Request.Types.AddChannel:
						AddChannel(request.Guid, request.Name);
						break;

					case Request.Types.RemoveChannel:
						RemoveChannel(request.Guid);
						break;

					case Request.Types.ActivateObject:
						ActivateObject(request.Guid);
						break;

					case Request.Types.DeactivateObject:
						DeactivateObject(request.Guid);
						break;

					case Request.Types.Search:
					case Request.Types.PacketsFromBot:
						OnSearch(currentUser, request);
						break;

					case Request.Types.SearchExternal:
						OnSearchExternal(currentUser, request);
						break;

					case Request.Types.AddSearch:
						OnAddSearch(request.Name);
						break;

					case Request.Types.RemoveSearch:
						OnRemoveSearch(request.Guid);
						break;

					case Request.Types.Searches:
						UnicastAdded(currentUser, Searches.All);
						break;

					case Request.Types.Servers:
						UnicastAdded(currentUser, Servers.All);
						break;

					case Request.Types.ChannelsFromServer:
						OnChannelsFromServer(currentUser, request);
						break;

					case Request.Types.LiveSnapshot:
						OnLiveSnapshot(currentUser);
						break;

					case Request.Types.Snapshots:
						OnSnapshots(currentUser, request);
						break;

					case Request.Types.Files:
						UnicastAdded(currentUser, Files.All);
						break;

					case Request.Types.CloseServer:
						break;

					case Request.Types.ParseXdccLink:
						OnParseXdccLink(request.Name);
						break;
				}
#if !UNSAFE
			}
			catch (Exception ex)
			{
				Log.Fatal("OnMessage(" + aContext.ConnectionInfo.ClientIpAddress + ", " + aMessage + ")", ex);
			}
#endif
		}

		void OnError(IWebSocketConnection aContext, Exception aException)
		{
			Log.Info("OnError(" + aContext.ConnectionInfo.ClientIpAddress + ")", aException);

			OnClose(aContext);
		}

		#endregion

		#region Websocket Write

		void Broadcast(Response.Types aType, Core.AObject aObject, bool aOnlySendVisibleObjects)
		{
			var response = new Response
			{
				Type = aType,
				Data = aObject
			};

			foreach (var user in _users.ToArray())
			{
				Unicast(user, response, aOnlySendVisibleObjects);
			}
		}

		void UnicastAdded(User aUser, IEnumerable<object> aObjects)
		{
			foreach (var obj in aObjects)
			{
				var response = new Response
				{
					Type = Response.Types.ObjectAdded,
					Data = obj
				};
				Unicast(aUser, response, false);
			}
		}

		void Unicast(User aUser, Response aResponse, bool aOnlySendVisibleObjects)
		{
			if (aOnlySendVisibleObjects)
			{
				if (aResponse.Data is Core.Bot || aResponse.Data is Core.Packet)
				{
					switch (aResponse.Type)
					{
						case Response.Types.ObjectAdded:
						case Response.Types.ObjectChanged:
							var bot = aResponse.Data as Core.Bot;
							if (bot != null && !IsVisible(bot, aUser.LastSearchRequest))
							{
								return;
							}
							var packet = aResponse.Data as Core.Packet;
							if (packet != null && !IsVisible(packet, aUser.LastSearchRequest))
							{
								return;
							}
							break;
					}
				}
			}

			if (aResponse.Data.GetType().IsSubclassOf(typeof(Core.AObject)))
			{
				Core.AObject data = (Core.AObject)aResponse.Data;
				// lock loaded objects to prevent sending out the same object more than once
				lock (aUser.LoadedObjects)
				{
					switch (aResponse.Type)
					{
						case Response.Types.ObjectAdded:
							if (aUser.LoadedObjects.Contains(data.Guid))
							{
								return;
							}
							aUser.LoadedObjects.Add(data.Guid);
							break;

						case Response.Types.ObjectChanged:
							if (!aUser.LoadedObjects.Contains(data.Guid))
							{
								return;
							}
							break;

						case Response.Types.ObjectRemoved:
							if (!aUser.LoadedObjects.Contains(data.Guid))
							{
								return;
							}
							aUser.LoadedObjects.Remove(data.Guid);
							break;
					}
				}
			}

			Object.AObject myObj = null;

			if (aResponse.Data is Core.Server)
			{
				myObj = new Object.Server { Object = aResponse.Data as Core.Server };
			}
			if (aResponse.Data is Core.Channel)
			{
				myObj = new Object.Channel { Object = aResponse.Data as Core.Channel };
			}
			if (aResponse.Data is Core.Bot)
			{
				myObj = new Object.Bot { Object = aResponse.Data as Core.Bot };
			}
			if (aResponse.Data is Core.Packet)
			{
				myObj = new Object.Packet { Object = aResponse.Data as Core.Packet };
			}
			if (aResponse.Data is Core.Search)
			{
				var search = aResponse.Data as Core.Search;
				var request = new Request {
					Type = Request.Types.Search,
					Guid = search.Guid,
					Name = search.Name
				};
				var results = from server in Servers.All from channel in server.Channels from bot in channel.Bots from packet in bot.Packets where IsVisible(packet, request) select packet;
				myObj = new Object.Search
				{
					Object = aResponse.Data as Core.Search,
					ResultsOnline = (from obj in results where obj is Core.Packet && obj.Parent.Connected select obj).Count(),
					ResultsOffline = (from obj in results where obj is Core.Packet && !obj.Parent.Connected select obj).Count()
				};
			}
			if (aResponse.Data is Core.Notification)
			{
				myObj = new Object.Notification { Object = aResponse.Data as Core.Notification };
			}
			if (aResponse.Data is Core.File)
			{
				myObj = new Object.File { Object = aResponse.Data as Core.File };
			}
			if (aResponse.Data is FilePart)
			{
				return;
			}

			if (myObj != null)
			{
				aResponse.Data = myObj;
			}

			string message = null;
			try
			{
				message = JsonConvert.SerializeObject(aResponse, _jsonSerializerSettings);
			}
			catch (Exception ex)
			{
				Log.Fatal("Unicast(" + aUser.Connection.ConnectionInfo.ClientIpAddress + ", " + aResponse.Type + "|" + aResponse.DataType + ")", ex);
			}

			if (message != null)
			{
#if !UNSAFE
				try
				{
#endif
					aUser.Connection.Send(message);
					Log.Info("Unicast(" + aUser.Connection.ConnectionInfo.ClientIpAddress + ", " + message + ")");
#if !UNSAFE
				}
				catch (Exception ex)
				{
					Log.Fatal("Unicast(" + aUser.Connection.ConnectionInfo.ClientIpAddress + ", " + message + ")", ex);
				}
#endif
			}
		}

		#endregion

		#region Functions

		void OnLiveSnapshot(User currentUser)
		{
			Unicast(currentUser, new Response
			{
				Type = Response.Types.LiveSnapshot,
				Data = GetFlotSnapshot()
			}, false);
		}

		void OnSnapshots(User currentUser, Request request)
		{
			var startTime = DateTime.Now.AddDays(int.Parse(request.Name));
			var data = GetFlotData(startTime, DateTime.Now);

			Unicast(currentUser, new Response
			{
				Type = Response.Types.Snapshots,
				Data = data
			}, false);
		}

		void OnChannelsFromServer(User currentUser, Request request)
		{
			var channels = (from server in Servers.All from channel in server.Channels where channel.ParentGuid == request.Guid select channel).ToList();
			UnicastAdded(currentUser, channels);
			var tServer = Servers.WithGuid(request.Guid);
			if (tServer != null)
			{
				Unicast(currentUser, new Response
				{
					Type = Response.Types.ObjectChanged,
					Data = tServer
				}, false);
			}
		}

		void OnAddSearch(string aSearch)
		{
			var obj = Searches.Named(aSearch);
			if (obj == null)
			{
				obj = new Core.Search { Name = aSearch };
				Searches.Add(obj);
			}
		}

		void OnRemoveSearch(Guid aGuid)
		{
			var search = Searches.WithGuid(aGuid);
			if (search != null)
			{
				Searches.Remove(search);
			}
		}

		void OnSearchExternal(User currentUser, Request request)
		{
			var searchExternal = Searches.WithGuid(request.Guid);
			if (searchExternal != null)
			{
				request.Name = searchExternal.Name;
			}

			var results = SearchExternal(request.Name);
			foreach (var result in results)
			{
				var currentResponse = new Response
				{
					Type = Response.Types.ObjectAdded,
					Data = result
				};
				Unicast(currentUser, currentResponse, false);
			}

			Unicast(currentUser, new Response
			{
				Type = Response.Types.SearchComplete,
				Data = request.Type
			}, false);
		}

		void OnSearch(User currentUser, Request request)
		{
			currentUser.LastSearchRequest = request;

			var allPackets = from server in Servers.All from channel in server.Channels from bot in channel.Bots from packet in bot.Packets where IsVisible(packet, request) select packet;
			var all = new List<Core.AObject>();
			all.AddRange(allPackets);
			all.AddRange(from packet in allPackets select packet.Parent);

			UnicastAdded(currentUser, all);

			Core.AObject update = null;
			if (request.Type == Request.Types.Search)
			{
				Unicast(currentUser, new Response
				{
					Type = Response.Types.SearchComplete,
					Data = request.Type
				}, false);
				// send search again to update search results
				update = Searches.WithGuid(request.Guid);
			}
			else if (request.Type == Request.Types.PacketsFromBot)
			{
				update = Servers.WithGuid(request.Guid) as Core.Bot;
			}

			if (update != null)
			{
				Unicast(currentUser, new Response
				{
					Type = Response.Types.ObjectChanged,
					Data = update
				}, false);
			}
		}

		void OnAddServer(String aName)
		{
			string serverString = aName;
			int port = 6667;
			if (serverString.Contains(":"))
			{
				string[] serverArray = serverString.Split(':');
				serverString = serverArray[0];
				port = int.Parse(serverArray[1]);
			}
			AddServer(serverString, port);
		}

		void OnParseXdccLink(String aLink)
		{
			string[] link = aLink.Substring(7).Split('/');
			string serverName = link[0];
			string channelName = link[2];
			string botName = link[3];
			int packetId = int.Parse(link[4].Substring(1));

			// checking server
			Core.Server serv = Servers.Server(serverName);
			if (serv == null)
			{
				Servers.Add(serverName);
				serv = Servers.Server(serverName);
			}
			serv.Enabled = true;

			// checking channel
			Core.Channel chan = serv.Channel(channelName);
			if (chan == null)
			{
				serv.AddChannel(channelName);
				chan = serv.Channel(channelName);
			}
			chan.Enabled = true;

			// checking bot
			Core.Bot tBot = chan.Bot(botName);
			if (tBot == null)
			{
				tBot = new Core.Bot { Name = botName };
				chan.AddBot(tBot);
			}

			// checking packet
			Core.Packet pack = tBot.Packet(packetId);
			if (pack == null)
			{
				pack = new Core.Packet { Id = packetId, Name = link[5] };
				tBot.AddPacket(pack);
			}
			pack.Enabled = true;
		}

		bool IsVisible(Core.Bot aBot, Request aRequest)
		{
			if (aRequest == null)
			{
				return false;
			}

			foreach (var packet in aBot.Packets)
			{
				if (IsVisible(packet, aRequest))
				{
					return true;
				}
			}

			return false;
		}

		bool IsVisible(Core.Packet aPacket, Request aRequest)
		{
			if (aRequest == null)
			{
				return false;
			}

			if (aRequest.Type == Request.Types.Search)
			{
				if (aRequest.Guid == Server._searchDownloads.Guid)
				{
					return aPacket.Connected;
				}

				if (aRequest.Guid == Server._searchEnabled.Guid)
				{
					return aPacket.Enabled;
				}

				var str = aRequest.Name;

				var search = Searches.WithGuid(aRequest.Guid);
				if (search != null)
				{
					str = search.Name;
				}

				return aPacket.Name.ContainsAll(str.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
			}

			if (aRequest.Type == Request.Types.PacketsFromBot)
			{
				return aPacket.ParentGuid == aRequest.Guid;
			}

			return false;
		}

		IEnumerable<Flot> GetFlotSnapshot ()
		{
			var tObjects = new List<Flot>();

			var snapshot = CollectSnapshot();
			for (int a = 1; a <= Snapshot.SnapshotCount; a++)
			{
				var value = (SnapshotValue) a;
				var obj = new Flot();
				obj.Data = new double[][]{ new double[]{snapshot.Get(SnapshotValue.Timestamp), snapshot.Get(value)}};
				obj.Label = Enum.GetName(typeof (SnapshotValue), value);

				tObjects.Add(obj);
			}

			return tObjects.ToArray();
		}

		IEnumerable<ExternalSearch> SearchExternal (string search)
		{
			var objects = new List<ExternalSearch>();

			int start = 0;
			int limit = 25;
			do
			{
				try
				{
					var uri = new Uri("http://xg.bitpir.at/index.php?show=search&action=external&xg=" + Settings.Instance.XgVersion + "&start=" + start + "&limit=" + limit + "&search=" + search);
					var req = HttpWebRequest.Create(uri);

					var response = req.GetResponse();
					StreamReader sr = new StreamReader(response.GetResponseStream());
					string text = sr.ReadToEnd();
					response.Close();

					ExternalSearch[] results = JsonConvert.DeserializeObject<ExternalSearch[]>(text, _jsonSerializerSettings);

					if (results.Length > 0)
					{
						objects.AddRange(results);
					}

					if (results.Length == 0 || results.Length < limit)
					{
						break;
					}
				}
				catch (Exception ex)
				{
					Log.Fatal("OnSearchExternal(" + search + ") cant load external search", ex);
					break;
				}
				start += limit;
			} while (true);

			return objects;
		}

		IEnumerable<Flot> GetFlotData(DateTime aStart, DateTime aEnd)
		{
			var tObjects = new List<Flot>();

			FetchData data = RrdDb.createFetchRequest(ConsolFuns.CF_AVERAGE, aStart.ToTimestamp(), aEnd.ToTimestamp(), 1).fetchData();
			Int64[] times = data.getTimestamps();
			double[][] values = data.getValues();

			for (int a = 1; a <= Snapshot.SnapshotCount; a++)
			{
				var value = (SnapshotValue) a;
				var obj = new Flot();

				var list = new List<double[]>();
				for (int b = 0; b < times.Length; b++)
				{
					double[] current = { times[b] * 1000, values[a][b] };
					list.Add(current);
				}
				obj.Data = list.ToArray();
				obj.Label = Enum.GetName(typeof (SnapshotValue), value);

				tObjects.Add(obj);
			}

			return tObjects.ToArray();
		}

		#endregion
	}
}

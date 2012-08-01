//  
//  Copyright (C) 2012 Lars Formella <ich@larsformella.de>
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
using NUnit.Framework;
using XG.Core;

namespace XG.Test
{
	[TestFixture()]
	public class XG_Core_Helper
	{
		[Test()]
		public void ShrinkFileName ()
		{
			string fileName = "This_(is).-an_Evil)(File-_-name_[Test].txt";
			Int64 fileSize = 440044;
			string result = XGHelper.ShrinkFileName(fileName, fileSize);

			Assert.AreEqual("thisisanevilfilenametesttxt.440044/", result);
		}
	}
}


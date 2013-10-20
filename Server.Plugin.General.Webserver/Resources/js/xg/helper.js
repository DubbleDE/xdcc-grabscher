// 
//  helper.js
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

define(['xg/config'], function(config)
{
	var self = {
		/**
		 * @param {int} size
		 * @param {int} decimal
		 * @return {String}
		 */
		size2Human: function (size, decimal)
		{
			if (decimal == undefined)
			{
				decimal = 0;
			}
			if (size == 0)
			{
				return "&nbsp;";
			}
			if (size < 1024)
			{
				return size + " B";
			}
			else if (size < 1024 * 1024)
			{
				return (size / 1024).toFixed(decimal) + " K";
			}
			else if (size < 1024 * 1024 * 1024)
			{
				return (size / (1024 * 1024)).toFixed(decimal) + " M";
			}
			else if (size < 1024 * 1024 * 1024 * 1024)
			{
				return (size / (1024 * 1024 * 1024)).toFixed(decimal) + " G";
			}
			return (size / (1024 * 1024 * 1024 * 1024)).toFixed((size < 1024 * 1024 * 1024 * 1024 * 10) ? 1 : decimal) + " T";
		},

		/**
		 * @param {int} speed
		 * @return {String}
		 */
		speed2Human: function (speed)
		{
			if (speed == 0)
			{
				return "&nbsp;";
			}
			if (speed < 1024)
			{
				return speed + " B";
			}
			else if (speed < 1024 * 1024)
			{
				return (speed > 100 * 1024 ? (speed / 1024).toFixed(1) : (speed / 1024).toFixed(2)) + " K";
			}
			return (speed > 100 * 1024 * 1024 ? (speed / (1024 * 1024)).toFixed(1) : (speed / (1024 * 1024)).toFixed(2)) + " M";
		},

		/**
		 * @param {String} date
		 * @return {String}
		 */
		date2Human: function (date)
		{
			var momentDate = moment(date);
			if (momentDate.year() == 1)
			{
				return "";
			}
			return config.getHumanDates() ? momentDate.fromNow() : momentDate.format("L LT");
		},

		/**
		 * @param {int} time
		 * @return {String}
		 */
		time2Human: function (time)
		{
			if (time <= 0 || time >= 106751991167300 || time == undefined || time == null)
			{
				return "";
			}

			return moment.duration(time, "seconds").humanize(true);
		}
	};

	return self;
});

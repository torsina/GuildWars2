﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;

using GuildWars2.ArenaNet.API;
using GuildWars2.ArenaNet.Model;

namespace GuildWars2.ArenaNet.Test
{
    public class Program
    {
        public static void Main(string[] args)
        {
            IDictionary<Guid, Event> event_names = new EventNamesRequest().Execute().Where(e => {
                string n = e.Name.ToLower();

                return (n.Contains("scarlet") || n.Contains("aetherblade") || n.Contains("molten"));
            }).ToDictionary(e => e.Id);
            IDictionary<int, Map> map_names = new MapNamesRequest().Execute().ToDictionary(m => m.Id);
            IDictionary<Guid, EventDetails> event_details = new EventDetailsRequest().Execute().Events;

            IDictionary<int, IList<Guid>> map_events = new Dictionary<int, IList<Guid>>();

            StreamWriter s = new StreamWriter(new FileStream(@"D:\tmp\stuff.txt", FileMode.Create));
            
            foreach (Guid eid in event_names.Keys)
            {
                if (!event_details.ContainsKey(eid))
                    continue;

                EventDetails details = event_details[eid];

                if (!map_events.ContainsKey(details.MapId))
                    map_events[details.MapId] = new List<Guid>();

                map_events[details.MapId].Add(eid);
            }

            foreach (int mapid in map_events.Keys)
            {
                s.WriteLine(string.Format("Scarlet events in [{0}]:", map_names[mapid].Name));

                foreach (Guid eid in map_events[mapid])
                {
                    s.WriteLine(string.Format("  [{0}]: [{1}]", eid, event_names[eid].Name));
                }

                s.WriteLine();
            }

            s.Close();
        }
    }
}

﻿using System;
using System.Collections.Generic;
using System.Linq;

using GuildWars2.ArenaNet.Model.V1;

namespace GuildWars2.GoMGoDS.Model
{
    public class MetaEventStage
    {
        public virtual StageType Type { get; private set; }
        public virtual string Name { get; private set; }
        public virtual bool IsEndStage { get; private set; }

        public virtual HashSet<EventState> EventStates { get; private set; }

        // 0 = no countdown
        // MaxValue = continue from previous
        // other = value in seconds
        public uint Countdown { get; private set; }

        public MetaEventStage(StageType type, string name, uint countdown = 0, bool isEndStage = false)
        {
            Type = type;
            Name = name;
            EventStates = new HashSet<EventState>();

            Countdown = countdown;

            IsEndStage = isEndStage;
        }

        public virtual MetaEventStage AddEvent(Guid ev)
        {
            return AddEvent(ev, EventStateType.Preparation)
                    .AddEvent(ev, EventStateType.Active);
        }

        public virtual MetaEventStage AddEvent(Guid ev, EventStateType state)
        {
            EventStates.Add(new EventState() { Event = ev, State = state });
            return this;
        }

        public virtual bool IsActive(HashSet<GuildWars2.ArenaNet.Model.V1.EventState> events)
        {
            return events.Where(es => EventStates.Contains(new EventState() { Event = es.EventId, State = es.StateEnum })).Count() > 0;
        }

        public virtual bool IsSuccessful(HashSet<GuildWars2.ArenaNet.Model.V1.EventState> events)
        {
            IEnumerable<Guid> eventIds = EventStates.Select(es => es.Event).Distinct();

            return events.Where(es => eventIds.Contains(es.EventId) && es.StateEnum == EventStateType.Success).Count() == eventIds.Count();
        }

        public virtual bool IsFailed(HashSet<GuildWars2.ArenaNet.Model.V1.EventState> events)
        {
            IEnumerable<Guid> eventIds = EventStates.Select(es => es.Event).Distinct();

            return events.Where(es => eventIds.Contains(es.EventId) && es.StateEnum == EventStateType.Fail).Count() > 0;
        }

        public struct EventState
        {
            public Guid Event;
            public EventStateType State;
        }

        public enum StageType
        {
            Reset,
            Invalid,
            Recovery,
            Blocking,
            PreEvent,
            Boss
        }
    }
}

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using GuildWars2.Spidy.Model;

namespace GuildWars2.Spidy.API
{
    public class GemPriceHistoryResponse : PaginatedResponse
    {
        public List<GemPriceLog> Results { get; set; }
    }
}

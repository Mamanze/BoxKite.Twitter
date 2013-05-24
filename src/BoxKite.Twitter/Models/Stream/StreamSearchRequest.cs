﻿// (c) 2012-2013 Nick Hodge mailto:hodgenick@gmail.com & Brendan Forster
// License: MS-PL

using System.Collections.Generic;

namespace BoxKite.Twitter.Models.Stream
{
    public class StreamSearchRequest
    {
        public List<string> Follows;
        public List<string> Tracks;
        public List<string> Locations;
        public string FilterLevel; //note this can be none,low,medium
        public string Language;

        public StreamSearchRequest()
        {
            Follows = new List<string>();
            Tracks = new List<string>();
            Locations = new List<string>();
            FilterLevel = "none";
            Language = "en";
        }
    }
}

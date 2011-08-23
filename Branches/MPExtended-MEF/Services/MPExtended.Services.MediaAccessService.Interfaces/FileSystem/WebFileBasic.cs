﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MPExtended.Services.MediaAccessService.Interfaces.Shared;

namespace MPExtended.Services.MediaAccessService.Interfaces.FileSystem
{
    public class WebFileBasic : WebMediaItem
    {
        public WebFileBasic()
        {
            DateAdded = new DateTime(1970, 1, 1);
        }
        public string Id { get; set; }
         public  IList<string> Path { get; set; }
        public DateTime DateAdded { get; set; }

        public WebMediaType Type
        {
            get
            {
                return WebMediaType.File;
            }
            set
            {
                Type = value;
            }
        }
    }
}
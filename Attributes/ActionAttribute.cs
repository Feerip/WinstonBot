﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinstonBot.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class ActionAttribute : Attribute
    {
        public string Name { get; set; }
        //public string Description { get; set; }

        public ActionAttribute()
        {
            Name = null;
            //Description = null;
        }

        public ActionAttribute(string name)
        {
            Name = name;
        }
    }
}

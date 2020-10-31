using System;
using System.Collections.Generic;
using System.Text;

namespace ProgrammerAl.DTO.Attributes
{
    /// <summary>
    /// Attribute used to mark a class to be included when generating DTO source classes
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class GenerateDtoAttribute : Attribute
    {
        public GenerateDtoAttribute()
        {
        }
    }
}

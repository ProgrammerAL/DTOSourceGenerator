using System;

namespace ProgrammerAl.DTO.Attributes
{
    /// <summary>
    /// Attribute for a property in a DTO to specify if a property in a class should allow null values when it is checked during the CheckIsValid() call. Default is false
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class BasicPropertyCheckAttribute : Attribute
    {
        public const bool DefaultAllowNull = false;

        public BasicPropertyCheckAttribute()
        {
            AllowNull = DefaultAllowNull;
        }

        public bool AllowNull { get; set; }
    }

    /// <summary>
    /// Attribute for a property in a DTO, where that property's data type is also a DTO, to specify is the CheckIsValid() method will be called when the parent class's CheckIsValid() is called. Default is true
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class DtoPropertyCheckAttribute : BasicPropertyCheckAttribute
    {
        public const bool DefaultCheckIsValid = true;

        public DtoPropertyCheckAttribute()
            : base()
        {
            CheckIsValid = DefaultCheckIsValid;
        }

        public bool CheckIsValid { get; set; }
    }

    /// <summary>
    /// Attribute for a string property in a DTO to specify what type of string data to allow when it is checked during the CheckIsValid() call. Default is Requires Non-Whitespace Text
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class StringPropertyCheckAttribute : Attribute
    {
        public const StringIsValidCheckType DefaultStringIsValidCheckType = StringIsValidCheckType.RequiresNonWhitespaceText;

        public StringPropertyCheckAttribute()
        {
            StringIsValidCheckType = DefaultStringIsValidCheckType;
        }

        public StringIsValidCheckType StringIsValidCheckType { get; set; }
    }

    public enum StringIsValidCheckType
    {
        Unknown,
        AllowNull,
        AllowEmptyString,
        AllowWhenOnlyWhiteSpace,
        RequiresNonWhitespaceText
    }
}

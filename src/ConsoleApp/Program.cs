using ProgrammerAl.DTO.Attributes;

using System;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace ConsoleApp
{
    [GenerateDto]
    public class MyDTOClass
    {
        [StringPropertyCheck(StringIsValidCheckType = StringIsValidCheckType.Unknown)]
        public string StringCheck_Unknown { get; set; }

        [StringPropertyCheck(StringIsValidCheckType = StringIsValidCheckType.AllowNull)]
        public string StringCheck_AllowNull { get; set; }

        [StringPropertyCheck(StringIsValidCheckType = StringIsValidCheckType.AllowEmptyString)]
        public string StringCheck_AllowEmptyString { get; set; }

        [StringPropertyCheck(StringIsValidCheckType = StringIsValidCheckType.AllowWhenOnlyWhiteSpace)]
        public string StringCheck_AllowWhenOnlyWhiteSpace { get; set; }

        [StringPropertyCheck(StringIsValidCheckType = StringIsValidCheckType.RequiresNonWhitespaceText)]
        public string StringCheck_RequiresNonWhitespaceText { get; set; }

        public int IntVal { get; set; }
        //Ignore nullables for now
        //public int? NullableIntVal { get; set; }

        //public MyChildClass MyChild { get; set; }
    }

    //[GenerateDTO]
    //public class MyChildClass
    //{
    //    public string FirstName { get; set; }
    //}

    //[ProgrammerAl.DTO.Utilities.Attributes]
    //public record MyRecord(string Name, int Val);

    //[SoapAttribute]
    //public class MyNonDTOClass
    //{
    //    public string Name { get; set; }
    //    public int Val { get; set; }
    //}

    class Program
    {
        static void Main(string[] args)
        {
            var abc = new ConsoleApp.MyDTOClassDTO()
            {
                StringCheck_Unknown = "unknown"
            };


            //var def = new ConsoleApp.MyRecordDTO()
            //{
            //    Name = null,
            //    Val = null
            //};
            _ = Console.ReadLine();
        }
    }
}

namespace System.Runtime.CompilerServices
{
    public class IsExternalInit { }
}
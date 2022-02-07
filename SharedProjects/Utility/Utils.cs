using System;
using System.Collections.Generic;
using System.Text;

namespace IngameScript
{
    public class SRKStringBuilder
    {
        StringBuilder myStringBuilderInternal;
        public SRKStringBuilder(int chars)
        {
            myStringBuilderInternal = new StringBuilder(chars);
        }
        public SRKStringBuilder AppendLine(string text)
        {
            myStringBuilderInternal.AppendLine(text);
            return this;
        }
        public SRKStringBuilder Append(string text)
        {
            myStringBuilderInternal.Append(text);
            return this;
        }

        public override string ToString() 
        {
            return myStringBuilderInternal.ToString();
        }
    }

//     public class MyTime
//     {
//         public static readonly TimeSpan Zero;
//         TimeSpan myTimespan;
//         
//     }

//     class Utils
//     {
//         
//     }
}

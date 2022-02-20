using System;
using System.Collections.Generic;
using System.Text;

namespace IngameScript
{
    public class SRKStringBuilder
    {
        public StringBuilder myStringBuilderInternal;
        public SRKStringBuilder(int chars = 256)
        {
            myStringBuilderInternal = new StringBuilder(chars);
        }
        public SRKStringBuilder AppendLine()
        {
            myStringBuilderInternal.AppendLine();
            return this;
        }
        public SRKStringBuilder AppendLine(string text)
        {
            myStringBuilderInternal.AppendLine(text);
            return this;
        }
        public SRKStringBuilder Append<T>(T text)
        {
            myStringBuilderInternal.Append(text);
            return this;
        }

        public SRKStringBuilder Append(char text, int repeat)
        {
            myStringBuilderInternal.Append(text, repeat);
            return this;
        }
        public void Clear() { myStringBuilderInternal.Clear(); }

        public int Length
        {
            get
            {
                return myStringBuilderInternal.Length;
            }
        }
        public override string ToString() 
        {
            return myStringBuilderInternal.ToString();
        }

        //public static implicit operator StringBuilder(SRKStringBuilder builder) { return builder.myStringBuilderInternal;  }
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

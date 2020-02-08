using System;
using System.Collections.Generic;
using System.Text;

namespace IngameScript
{
    public class MyStringReader
    {
        int _currentLine = 0;
        string[] a;

        public string NextLine()
        {
            _currentLine += 1;
            if (a.Length >= _currentLine)
                return a[_currentLine - 1];
            return String.Empty;
        }

        public MyStringReader(string s)
        {
            a = s.Split(new[] { "\r\n", "\r", "\n" },StringSplitOptions.None);
        }
    }
}

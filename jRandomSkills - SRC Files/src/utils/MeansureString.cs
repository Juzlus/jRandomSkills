using CounterStrikeSharp.API;
using System.Text.RegularExpressions;

namespace src.player;

public static class MeansureString
{
    static readonly float DefaultCharWidth = 22.25f;
    static readonly Dictionary<char, float> CharWidth = new()
    {
        ['0'] = 22.25f,
        ['1'] = 22.25f,
        ['2'] = 22.25f,
        ['3'] = 22.25f,
        ['4'] = 22.25f,
        ['5'] = 22.25f,
        ['6'] = 22.25f,
        ['7'] = 22.25f,
        ['8'] = 22.25f,
        ['9'] = 22.25f,
        ['a'] = 22.25f,
        ['b'] = 22.25f,
        ['c'] = 20.00f,
        ['d'] = 22.25f,
        ['e'] = 22.25f,
        ['f'] = 11.11f,
        ['g'] = 22.25f,
        ['h'] = 22.25f,
        ['i'] = 8.89f,
        ['j'] = 8.89f,
        ['k'] = 20.00f,
        ['l'] = 8.89f,
        ['m'] = 33.33f,
        ['n'] = 22.25f,
        ['o'] = 22.25f,
        ['p'] = 22.25f,
        ['q'] = 22.25f,
        ['r'] = 13.33f,
        ['s'] = 20.00f,
        ['t'] = 11.11f,
        ['u'] = 22.25f,
        ['v'] = 20.00f,
        ['w'] = 28.89f,
        ['x'] = 20.00f,
        ['y'] = 20.00f,
        ['z'] = 20.00f,
        ['A'] = 26.69f,
        ['B'] = 26.69f,
        ['C'] = 28.89f,
        ['D'] = 28.89f,
        ['E'] = 26.69f,
        ['F'] = 24.44f,
        ['G'] = 31.11f,
        ['H'] = 28.89f,
        ['I'] = 11.11f,
        ['J'] = 20.00f,
        ['K'] = 26.69f,
        ['L'] = 22.25f,
        ['M'] = 33.33f,
        ['N'] = 28.89f,
        ['O'] = 31.11f,
        ['P'] = 26.69f,
        ['Q'] = 31.11f,
        ['R'] = 28.89f,
        ['S'] = 26.69f,
        ['T'] = 24.44f,
        ['U'] = 28.89f,
        ['V'] = 26.69f,
        ['W'] = 37.75f,
        ['X'] = 26.69f,
        ['Y'] = 26.69f,
        ['Z'] = 24.44f,
        ['\''] = 7.64f,
        ['!'] = 11.11f,
        ['@'] = 40.61f,
        ['#'] = 22.25f,
        ['$'] = 22.25f,
        ['%'] = 35.56f,
        ['^'] = 18.77f,
        ['&'] = 26.69f,
        ['*'] = 15.56f,
        ['('] = 13.33f,
        [')'] = 13.33f,
        ['_'] = 22.25f,
        ['-'] = 13.33f,
        ['+'] = 23.36f,
        ['='] = 23.36f,
        [','] = 11.11f,
        ['.'] = 11.11f,
        [';'] = 11.11f,
        [':'] = 11.11f,
        ['<'] = 23.36f,
        ['>'] = 23.36f,
        ['/'] = 11.11f,
        ['?'] = 22.25f,
        ['\\'] = 11.11f,
        ['|'] = 10.39f,
        ['`'] = 13.33f,
        ['~'] = 23.36f,
        ['"'] = 14.20f,
        [' '] = 11.11f,
        ['―'] = 40.00f,
        ['◢'] = 30.00f,
        ['◆'] = 30.00f,
        ['◤'] = 30.00f,
    };

    private static float GetWidth(string s)
    {
        float w = 0;
        foreach (char c in s)
            w += CharWidth.TryGetValue(c, out var cw) ? cw : DefaultCharWidth;
        return w;
    }

    private static string StripColors(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        return Regex.Replace(s, @"[\x01-\x10]", string.Empty);
    }

    public static string GetTextDashed(string content, float targetWidth, char symbol, string color)
    {
        string cleanContent = StripColors(content);
        float contentWidth = GetWidth(cleanContent);
        float dashWidth = CharWidth.TryGetValue(symbol, out var cw) ? cw : DefaultCharWidth;

        float remaining = targetWidth - contentWidth;

        if (remaining <= 0) return content;
        int totalDashes = (int)Math.Floor(remaining / dashWidth);

        int left = totalDashes / 2;
        int right = totalDashes - left;

        return color + new string(symbol, left) + content + color + new string(symbol, right);
    }
}
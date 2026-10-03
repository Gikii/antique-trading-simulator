using System.Text;

namespace AntiqueTradingSimulator.Helpers
{
    public static class TextHelper
    {
        private const char TokenOpen = '{';
        private const char TokenClose = '}';
        private const char TokenSeparator = '|';
        public static string Resolve(string text, bool future)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(TokenOpen) < 0) return text;

            var builder = new StringBuilder(text.Length);
            int index = 0;

            while (index < text.Length)
            {
                if (text[index] != TokenOpen)
                {
                    AppendWithoutDoubleSpace(builder, text[index]);
                    index++;
                    continue;
                }

                int closing = text.IndexOf(TokenClose, index + 1);
                if (closing < 0)
                {
                    builder.Append(text, index, text.Length - index);
                    break;
                }

                string token = text.Substring(index + 1, closing - index - 1);
                int separator = token.IndexOf(TokenSeparator);

                string chosen = separator < 0
                    ? token
                    : future
                        ? token.Substring(separator + 1)
                        : token.Substring(0, separator);

                foreach (char c in chosen)
                    AppendWithoutDoubleSpace(builder, c);

                index = closing + 1;
            }

            return builder.ToString().Trim();
        }

        public static string Present(string text) => Resolve(text, future: false);

        public static string Future(string text) => Resolve(text, future: true);

        private static void AppendWithoutDoubleSpace(StringBuilder builder, char c)
        {
            if (c == ' ' && builder.Length > 0 && builder[builder.Length - 1] == ' ') return;
            builder.Append(c);
        }
    }
}
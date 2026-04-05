using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ConsoleANSI
{
    /// <summary>
    /// ANSI艺术字符控制台处理器
    /// </summary>
    public class ConsoleAnsiArtist
    {
        private static readonly Dictionary<char, string[]> _ansiArtLibrary = new Dictionary<char, string[]>
        {
            {
                'A', new string[]
                {
                    " █████╗ ",
                    "██╔══██╗",
                    "███████║",
                    "██╔══██║",
                    "██║  ██║",
                    "╚═╝  ╚═╝"
                }
            },
            {
                'B', new string[]
                {
                    "██████╗ ",
                    "██╔══██╗",
                    "██████╔╝",
                    "██╔══██╗",
                    "██████╔╝",
                    "╚═════╝ "
                }
            },
            {
                'C', new string[]
                {
                    " ██████╗",
                    "██╔════╝",
                    "██║     ",
                    "██║     ",
                    "╚██████╗",
                    " ╚═════╝"
                }
            },
            {
                'D', new string[]
                {
                    "██████╗ ",
                    "██╔══██╗",
                    "██║  ██║",
                    "██║  ██║",
                    "██████╔╝",
                    "╚═════╝ "
                }
            },
            {
                'E', new string[]
                {
                    "███████╗",
                    "██╔════╝",
                    "█████╗  ",
                    "██╔══╝  ",
                    "███████╗",
                    "╚══════╝"
                }
            },
            {
                'F', new string[]
                {
                    "███████╗",
                    "██╔════╝",
                    "█████╗  ",
                    "██╔══╝  ",
                    "██║     ",
                    "╚═╝     "
                }
            },
            {
                'G', new string[]
                {
                    " ██████╗ ",
                    "██╔════╝ ",
                    "██║  ███╗",
                    "██║   ██║",
                    "╚██████╔╝",
                    " ╚═════╝ "
                }
            },
            {
                'H', new string[]
                {
                    "██╗  ██╗",
                    "██║  ██║",
                    "███████║",
                    "██╔══██║",
                    "██║  ██║",
                    "╚═╝  ╚═╝"
                }
            },
            {
                'I', new string[]
                {
                    "██╗",
                    "██║",
                    "██║",
                    "██║",
                    "██║",
                    "╚═╝"
                }
            },
            {
                'J', new string[]
                {
                    "     ██╗",
                    "     ██║",
                    "     ██║",
                    "██   ██║",
                    "╚█████╔╝",
                    " ╚════╝ "
                }
            },
            {
                'K', new string[]
                {
                    "██╗  ██╗",
                    "██║ ██╔╝",
                    "█████╔╝ ",
                    "██╔═██╗ ",
                    "██║  ██╗",
                    "╚═╝  ╚═╝"
                }
            },
            {
                'L', new string[]
                {
                    "██╗     ",
                    "██║     ",
                    "██║     ",
                    "██║     ",
                    "███████╗",
                    "╚══════╝"
                }
            },
            {
                'M', new string[]
                {
                    "███╗   ███╗",
                    "████╗ ████║",
                    "██╔████╔██║",
                    "██║╚██╔╝██║",
                    "██║ ╚═╝ ██║",
                    "╚═╝     ╚═╝"
                }
            },
            {
                'N', new string[]
                {
                    "███╗   ██╗",
                    "████╗  ██║",
                    "██╔██╗ ██║",
                    "██║╚██╗██║",
                    "██║ ╚████║",
                    "╚═╝  ╚═══╝"
                }
            },
            {
                'O', new string[]
                {
                    " ██████╗ ",
                    "██╔═══██╗",
                    "██║   ██║",
                    "██║   ██║",
                    "╚██████╔╝",
                    " ╚═════╝ "
                }
            },
            {
                'P', new string[]
                {
                    "██████╗ ",
                    "██╔══██╗",
                    "██████╔╝",
                    "██╔═══╝ ",
                    "██║     ",
                    "╚═╝     "
                }
            },
            {
                'Q', new string[]
                {
                    " ██████╗ ",
                    "██╔═══██╗",
                    "██║   ██║",
                    "██║▄▄ ██║",
                    "╚██████╔╝",
                    " ╚══▀▀═╝ "
                }
            },
            {
                'R', new string[]
                {
                    "██████╗ ",
                    "██╔══██╗",
                    "██████╔╝",
                    "██╔══██╗",
                    "██║  ██║",
                    "╚═╝  ╚═╝"
                }
            },
            {
                'S', new string[]
                {
                    " ███████╗",
                    "██╔═════╝",
                    "███████╗ ",
                    "╚════██║ ",
                    "███████║ ",
                    "╚══════╝ "
                }
            },
            {
                'T', new string[]
                {
                    "████████╗",
                    "╚══██╔══╝",
                    "   ██║   ",
                    "   ██║   ",
                    "   ██║   ",
                    "   ╚═╝   "
                }
            },
            {
                'U', new string[]
                {
                    "██╗   ██╗",
                    "██║   ██║",
                    "██║   ██║",
                    "██║   ██║",
                    "╚██████╔╝",
                    " ╚═════╝ "
                }
            },
            {
                'V', new string[]
                {
                    "██╗   ██╗",
                    "██║   ██║",
                    "██║   ██║",
                    "╚██╗ ██╔╝",
                    " ╚████╔╝ ",
                    "  ╚═══╝  "
                }
            },
            {
                'W', new string[]
                {
                    "██╗    ██╗",
                    "██║    ██║",
                    "██║ █╗ ██║",
                    "██║███╗██║",
                    "╚███╔███╔╝",
                    " ╚══╝╚══╝ "
                }
            },
            {
                'X', new string[]
                {
                    "██╗  ██╗",
                    "╚██╗██╔╝",
                    " ╚███╔╝ ",
                    " ██╔██╗ ",
                    "██╔╝ ██╗",
                    "╚═╝  ╚═╝"
                }
            },
            {
                'Y', new string[]
                {
                    "██╗   ██╗",
                    "╚██╗ ██╔╝",
                    " ╚████╔╝ ",
                    "  ╚██╔╝  ",
                    "   ██║   ",
                    "   ╚═╝   "
                }
            },
            {
                'Z', new string[]
                {
                    "███████╗",
                    "╚══███╔╝",
                    "  ███╔╝ ",
                    " ███╔╝  ",
                    "███████╗",
                    "╚══════╝"
                }
            },
            // 标点符号
            {
                '"', new string[]
                {
                    "██╗   ██╗",
                    "██║   ██║",
                    "╚═╝   ╚═╝",
                    "        ",
                    "        ",
                    "        "
                }
            },
            {
                '\'', new string[]
                {
                    "██╗",
                    "██║",
                    "╚═╝",
                    "   ",
                    "   ",
                    "   "
                }
            },
            {
                ':', new string[]
                {
                    "   ",
                    "██╗",
                    "╚═╝",
                    "██╗",
                    "╚═╝",
                    "   "
                }
            },
            {
                ',', new string[]
                {
                    "   ",
                    "   ",
                    "   ",
                    "██╗",
                    "██║",
                    "╚═╝"
                }
            },
            {
                '。', new string[]
                {
                    "   ",
                    "   ",
                    "██╗",
                    "╚═╝",
                    "   ",
                    "   "
                }
            },
            {
                '.', new string[]
                {
                    "   ",
                    "   ",
                    "   ",
                    "   ",
                    "██╗",
                    "╚═╝"
                }
            },
            {
                '？', new string[]
                {
                    " ██████╗ ",
                    "██╔═══██╗",
                    "     ██╔╝",
                    "   ██╔╝  ",
                    "   ╚═╝   ",
                    "   ██╗   "
                }
            },
            {
                '?', new string[]
                {
                    " ██████╗ ",
                    "██╔═══██╗",
                    "     ██╔╝",
                    "   ██╔╝  ",
                    "   ╚═╝   ",
                    "   ██╗   "
                }
            },
            {
                '/', new string[]
                {
                    "     ██╗",
                    "    ██╔╝",
                    "   ██╔╝ ",
                    "  ██╔╝  ",
                    " ██╔╝   ",
                    "██╔╝    "
                }
            },
            {
                '!', new string[]
                {
                    "██╗",
                    "██║",
                    "██║",
                    "██║",
                    "╚═╝",
                    "██╗"
                }
            },
            {
                ';', new string[]
                {
                    "   ",
                    "██╗",
                    "╚═╝",
                    "██╗",
                    "██║",
                    "╚═╝"
                }
            }
        };

        /// <summary>
        /// 打印ANSI艺术文本
        /// </summary>
        /// <param name="text">要打印的文本</param>
        public static void PrintAnsiText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                Console.WriteLine("输入文本为空！");
                return;
            }

            // 检查文本是否完全由ANSI艺术字符库中的字符组成
            bool canPrintAsAnsi = text.All(c => _ansiArtLibrary.ContainsKey(GetCharKey(c)) || 
                                               c == ' ' || 
                                               IsChineseCharacter(c));
            
            // 如果包含不支持的字符（除了空格和中文字符），直接输出原文本
            if (!canPrintAsAnsi)
            {
                Console.WriteLine(text);
                return;
            }

            // 获取字符的最大高度
            int maxHeight = 0;
            foreach (char c in text)
            {
                char key = GetCharKey(c);
                if (_ansiArtLibrary.ContainsKey(key))
                {
                    maxHeight = Math.Max(maxHeight, _ansiArtLibrary[key].Length);
                }
            }

            // 如果maxHeight为0（比如只有空格或中文），直接输出
            if (maxHeight == 0)
            {
                Console.WriteLine(text);
                return;
            }

            // 逐行打印每个字符的对应行
            for (int line = 0; line < maxHeight; line++)
            {
                StringBuilder lineBuilder = new StringBuilder();
                
                foreach (char c in text)
                {
                    char key = GetCharKey(c);
                    
                    if (c == ' ')
                    {
                        // 空格处理
                        lineBuilder.Append("   ");
                    }
                    else if (IsChineseCharacter(c) && !_ansiArtLibrary.ContainsKey(c))
                    {
                        // 中文字符（除了已定义的标点），只在第一行显示，其他行留空
                        if (line == 0)
                        {
                            lineBuilder.Append(c).Append(" ");
                        }
                        else
                        {
                            lineBuilder.Append("  ");
                        }
                    }
                    else if (_ansiArtLibrary.ContainsKey(key))
                    {
                        var artLines = _ansiArtLibrary[key];
                        if (line < artLines.Length)
                        {
                            lineBuilder.Append(artLines[line]);
                        }
                        else
                        {
                            // 如果字符高度不够，用空格填充
                            if (artLines.Length > 0)
                                lineBuilder.Append(new string(' ', artLines[0].Length));
                            else
                                lineBuilder.Append("   ");
                        }
                        
                        // 字符间添加一个空格
                        lineBuilder.Append(' ');
                    }
                }
                
                Console.WriteLine(lineBuilder.ToString());
            }
        }

        /// <summary>
        /// 获取字符的键（处理大小写和特殊字符映射）
        /// </summary>
        private static char GetCharKey(char c)
        {
            // 特殊字符直接返回原字符
            if (IsSpecialSymbol(c))
            {
                return c;
            }
            
            // 字母转换为大写
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
            {
                return char.ToUpper(c);
            }
            
            return c;
        }

        /// <summary>
        /// 检查字符是否为特殊标点符号
        /// </summary>
        private static bool IsSpecialSymbol(char c)
        {
            return c == '"' || c == '\'' || c == ':' || c == ',' || c == '。' || 
                   c == '.' || c == '？' || c == '?' || c == '/' || c == '!' || c == ';';
        }

        /// <summary>
        /// 检查字符是否为中文字符
        /// </summary>
        private static bool IsChineseCharacter(char c)
        {
            // 中文字符的Unicode范围
            return (c >= 0x4E00 && c <= 0x9FFF) ||
                   (c >= 0x3400 && c <= 0x4DBF) ||
                   (c >= 0x20000 && c <= 0x2A6DF) ||
                   (c >= 0x2A700 && c <= 0x2B73F) ||
                   (c >= 0x2B740 && c <= 0x2B81F) ||
                   (c >= 0x2B820 && c <= 0x2CEAF) ||
                   (c >= 0xF900 && c <= 0xFAFF) ||
                   (c >= 0x2F800 && c <= 0x2FA1F);
        }

        /// <summary>
        /// 添加自定义ANSI艺术字符
        /// </summary>
        /// <param name="character">字符</param>
        /// <param name="artLines">艺术字符的每一行</param>
        public static void AddCustomCharacter(char character, string[] artLines)
        {
            if (artLines == null || artLines.Length == 0)
                throw new ArgumentException("艺术字符行不能为空");
            
            _ansiArtLibrary[character] = artLines;
        }
    }
}
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HMI.Core.Interface;

namespace HMI.Core.Services;

/// <summary>
/// §4.4 表达式引擎：支持 `${VarId}` 变量引用、比较（&gt; &lt; &gt;= &lt;= == !=）、逻辑（&amp;&amp; ||）、括号优先级。
/// 求值异常记 false；命名对齐契约束 IExpressionEngine.Evaluate 单入口。
/// </summary>
public sealed partial class ExpressionEngine : IExpressionEngine
{
    private static readonly Regex VarRef = VarRefRegex();

    [GeneratedRegex(@"\$\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled)]
    private static partial Regex VarRefRegex();

    /// <inheritdoc/>
    public ICompiledExpression Compile(string expression)
    {
        var deps = new HashSet<string>();
        foreach (Match m in VarRef.Matches(expression)) deps.Add(m.Groups[1].Value);
        return new CompiledExpr(expression, deps.ToArray());
    }

    /// <inheritdoc/>
    public bool Evaluate(string expression, object? context)
        => Evaluate(expression, context as IExpressionRuntimeContext ?? new MapContext(context));

    /// <inheritdoc/>
    public bool Evaluate(string expression, IExpressionRuntimeContext context)
    {
        try
        {
            var tokens = Tokenize(expression, context);
            var parser = new Parser(tokens);
            var result = parser.Parse();
            return result is bool b && b;
        }
        catch
        {
            return false; // §4.4：表达式异常视为 false
        }
    }

    private sealed class CompiledExpr(string source, IReadOnlyList<string> deps) : ICompiledExpression
    {
        public IReadOnlyList<string> DependencyVariableIds => deps;
        private readonly string _src = source;
        public override string ToString() => _src;
    }

    private sealed class MapContext(object? value) : IExpressionRuntimeContext
    {
        private readonly object? _value = value;
        public bool TryGetValue(string varId, out object? v)
        {
            v = _value;
            return true;
        }
    }

    // ---------- 词法分析 ----------
    private enum TokKind { Num, Str, Var, Bool, Op, LParen, RParen, End }

    private sealed record Token(TokKind Kind, object? Value, int Pos);

    private static List<Token> Tokenize(string expr, IExpressionRuntimeContext ctx)
    {
        var tokens = new List<Token>();
        var i = 0;
        var n = expr.Length;
        while (i < n)
        {
            var c = expr[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '(') { tokens.Add(new Token(TokKind.LParen, "(", i)); i++; continue; }
            if (c == ')') { tokens.Add(new Token(TokKind.RParen, ")", i)); i++; continue; }

            if (expr.AsSpan(i).StartsWith("&&")) { tokens.Add(new Token(TokKind.Op, "&&", i)); i += 2; continue; }
            if (expr.AsSpan(i).StartsWith("||")) { tokens.Add(new Token(TokKind.Op, "||", i)); i += 2; continue; }
            if (expr.AsSpan(i).StartsWith(">=")) { tokens.Add(new Token(TokKind.Op, ">=", i)); i += 2; continue; }
            if (expr.AsSpan(i).StartsWith("<=")) { tokens.Add(new Token(TokKind.Op, "<=", i)); i += 2; continue; }
            if (expr.AsSpan(i).StartsWith("==")) { tokens.Add(new Token(TokKind.Op, "==", i)); i += 2; continue; }
            if (expr.AsSpan(i).StartsWith("!=")) { tokens.Add(new Token(TokKind.Op, "!=", i)); i += 2; continue; }
            if (c is '>' or '<') { tokens.Add(new Token(TokKind.Op, c.ToString(), i)); i++; continue; }

            if (c == '$' && i + 1 < n && expr[i + 1] == '{')
            {
                var end = expr.IndexOf('}', i);
                if (end < 0) break;
                var varId = expr[(i + 2)..end].Trim();
                object? val = ctx.TryGetValue(varId, out var v) ? v : false;
                tokens.Add(new Token(TokKind.Var, val, i));
                i = end + 1;
                continue;
            }

            if (c == '"' || c == '\'')
            {
                var end = expr.IndexOf(c, i + 1);
                if (end < 0) break;
                var s = expr[(i + 1)..end];
                tokens.Add(new Token(TokKind.Str, s, i));
                i = end + 1;
                continue;
            }

            if (char.IsDigit(c) || c == '.')
            {
                var end = i;
                while (end < n && (char.IsDigit(expr[end]) || expr[end] == '.')) end++;
                var numStr = expr[i..end];
                if (double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                {
                    var isBool = num == 0 || num == 1;
                    tokens.Add(new Token(isBool ? TokKind.Bool : TokKind.Num, num, i));
                }
                i = end;
                continue;
            }

            i++;
        }
        tokens.Add(new Token(TokKind.End, null, n));
        return tokens;
    }

    // ---------- 语法分析（递归下降）----------
    private sealed class Parser(List<Token> tokens)
    {
        private int _pos;
        private Token Cur => tokens[_pos];

        public object Parse() => ParseOr();

        private object ParseOr()
        {
            var left = ParseAnd();
            while (Cur is { Kind: TokKind.Op, Value: "||" })
            {
                _pos++;
                var right = ParseAnd();
                left = ToBool(left) || ToBool(right);
            }
            return left;
        }

        private object ParseAnd()
        {
            var left = ParseEquality();
            while (Cur is { Kind: TokKind.Op, Value: "&&" })
            {
                _pos++;
                var right = ParseEquality();
                left = ToBool(left) && ToBool(right);
            }
            return left;
        }

        private object ParseEquality()
        {
            var left = ParseRelational();
            while (Cur is { Kind: TokKind.Op, Value: "==" or "!=" })
            {
                var op = (string)Cur.Value!;
                _pos++;
                var right = ParseRelational();
                var eq = Equal(left, right);
                left = op == "==" ? eq : !eq;
            }
            return left;
        }

        private object ParseRelational()
        {
            var left = ParsePrimary();
            while (Cur is { Kind: TokKind.Op, Value: ">" or ">=" or "<" or "<=" })
            {
                var op = (string)Cur.Value!;
                _pos++;
                var right = ParsePrimary();
                left = Compare(op, left, right);
            }
            return left;
        }

        private object ParsePrimary()
        {
            var t = Cur;
            switch (t.Kind)
            {
                case TokKind.LParen:
                    _pos++;
                    var inner = ParseOr();
                    if (Cur.Kind == TokKind.RParen) _pos++;
                    return inner;
                case TokKind.Num:
                case TokKind.Str:
                case TokKind.Bool:
                case TokKind.Var:
                    _pos++;
                    return t.Value ?? false;
                default:
                    _pos++;
                    return false;
            }
        }

        private static bool ToBool(object v) => v switch
        {
            bool b => b,
            double d => d != 0,
            string s => !string.IsNullOrEmpty(s),
            _ => false,
        };

        private static bool Equal(object l, object r)
        {
            if (IsNum(l) && IsNum(r)) return ToNum(l) == ToNum(r);
            return ToStr(l) == ToStr(r);
        }

        private static bool Compare(string op, object l, object r)
        {
            var a = ToNum(l);
            var b = ToNum(r);
            return op switch
            {
                ">" => a > b,
                ">=" => a >= b,
                "<" => a < b,
                _ => a <= b,
            };
        }

        private static bool IsNum(object v) => v is double && !double.IsNaN((double)v);
        private static double ToNum(object v) => v switch
        {
            double d => d,
            bool b => b ? 1 : 0,
            int i => i,
            long l => l,
            string s => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : double.NaN,
            _ => double.NaN,
        };
        private static string ToStr(object v) => v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
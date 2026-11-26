using System.Globalization;
using System.Text;

namespace BitMagic.X16Basic;

public sealed class BasicExpressionException(string message) : Exception(message);

/// <summary>
/// Somewhere a value can be written: a simple variable or an array element.
/// </summary>
public sealed record BasicTarget(string Expression, BasicVariableType Type, int Address, BasicRomSymbols Symbols)
{
    /// <summary>
    /// Writes a value. Numbers go straight in; strings are put in BASIC's string space below
    /// <c>fretop</c>, as BASIC itself does, and the old one is left for its garbage collector.
    /// </summary>
    public void Write(IMemoryWriter memory, BasicValue value)
    {
        switch (Type)
        {
            case BasicVariableType.Float or BasicVariableType.Integer:
                if (value is BasicString)
                    throw new BasicExpressionException($"{Expression} is a number.");
                BasicVariables.WriteNumber(memory, Type, Address, BasicExpression.ToNumber(value));
                return;

            case BasicVariableType.String:
                if (value is not BasicString s)
                    throw new BasicExpressionException($"{Expression} is a string.");
                if (s.Bytes.Length > 255)
                    throw new BasicExpressionException("Strings can be at most 255 characters.");

                var fretop = memory.ReadWord(Symbols.Fretop);
                var strend = memory.ReadWord(Symbols.Strend);
                var newTop = fretop - s.Bytes.Length;
                if (newTop < strend)
                    throw new BasicExpressionException("There isn't room in BASIC's string space.");

                memory.WriteBytes(newTop, s.Bytes);
                memory.WriteWord(Symbols.Fretop, newTop);
                memory.WriteByte(Address, (byte)s.Bytes.Length);
                memory.WriteWord(Address + 1, s.Bytes.Length == 0 ? 0 : newTop);
                return;

            default:
                throw new BasicExpressionException($"{Expression} can't be changed.");
        }
    }
}

/// <summary>
/// Evaluates BASIC expressions against a program's variables, for watches, hovers, breakpoint
/// conditions and log points. Supports numbers (including <c>$hex</c> and <c>%binary</c>), strings,
/// variables, array elements, the arithmetic, comparison and logical operators with BASIC's
/// precedence, and the common functions. Nothing it does changes the machine.
///
/// As in BASIC, comparisons give -1 for true and 0 for false, and a variable that doesn't exist
/// yet is 0 or "". The exception is an expression that is only a name: that throws if there's no
/// such variable, so hovering over a keyword doesn't show 0.
/// </summary>
public sealed class BasicExpression(IMemoryReader memory, BasicRomSymbols symbols, PetsciiCase charset = PetsciiCase.Upper)
{
    private string _text = "";
    private int _pos;

    /// <param name="nameMustExist">
    /// If the expression is only a name, throw if there's no such variable rather than give 0 or "".
    /// Right for watches and hovers; log points and conditions follow BASIC and pass false.
    /// </param>
    public BasicValue Evaluate(string expression, bool nameMustExist = true)
    {
        _text = expression;
        _pos = 0;

        if (nameMustExist && IsName(expression.Trim()) && !Exists(expression.Trim()))
            throw new BasicExpressionException($"{expression.Trim().ToUpperInvariant()} isn't a variable yet.");

        var value = ParseOr();
        SkipSpaces();
        if (_pos < _text.Length)
            throw new BasicExpressionException($"Didn't expect '{_text[_pos..]}'.");
        return value;
    }

    public bool IsTrue(string expression) => ToNumber(Evaluate(expression, false)) != 0;

    /// <summary>
    /// What <paramref name="expression"/> refers to, if it's a variable or array element.
    /// </summary>
    public BasicTarget? ResolveTarget(string expression)
    {
        _text = expression;
        _pos = 0;
        SkipSpaces();
        var name = ReadName() ?? throw new BasicExpressionException($"'{expression}' isn't a variable.");
        SkipSpaces();

        BasicTarget? target;
        if (Peek('('))
        {
            var array = BasicVariables.FindArray(memory, symbols, name) ?? throw new BasicExpressionException($"There's no array {name.ToUpperInvariant()}.");
            var subscripts = ReadSubscripts();
            target = new BasicTarget($"{name.ToUpperInvariant()}({string.Join(",", subscripts)})", array.Type, ElementAddress(array, subscripts), symbols);
        }
        else
        {
            var variable = BasicVariables.FindVariable(memory, symbols, name) ?? throw new BasicExpressionException($"{name.ToUpperInvariant()} isn't a variable yet.");
            target = new BasicTarget(variable.Name, variable.Type, variable.ValueAddress, symbols);
        }

        SkipSpaces();
        return _pos == _text.Length ? target : null;
    }

    public static bool IsName(string text) =>
        text.Length > 0 && char.IsAsciiLetter(text[0]) &&
        text.Take(text.Length - 1).All(char.IsAsciiLetterOrDigit) &&
        (char.IsAsciiLetterOrDigit(text[^1]) || text[^1] is '$' or '%');

    private bool Exists(string name) =>
        BasicVariables.FindVariable(memory, symbols, name) != null || BasicVariables.FindArray(memory, symbols, name) != null;

    public static double ToNumber(BasicValue value) => value switch
    {
        BasicNumber n => n.Value,
        BasicInteger i => i.Value,
        _ => throw new BasicExpressionException("Type mismatch: expected a number.")
    };

    private static BasicString ToStringValue(BasicValue value) =>
        value as BasicString ?? throw new BasicExpressionException("Type mismatch: expected a string.");

    private static BasicNumber Bool(bool b) => new(b ? -1 : 0);

    #region Parser

    private void SkipSpaces()
    {
        while (_pos < _text.Length && _text[_pos] == ' ')
            _pos++;
    }

    private bool Peek(char c)
    {
        SkipSpaces();
        return _pos < _text.Length && _text[_pos] == c;
    }

    private bool Accept(string token)
    {
        SkipSpaces();
        if (string.Compare(_text, _pos, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return false;

        // a word operator must not run into a name, so ORDER isn't OR DER
        if (char.IsAsciiLetter(token[^1]) && _pos + token.Length < _text.Length && char.IsAsciiLetterOrDigit(_text[_pos + token.Length]))
            return false;

        _pos += token.Length;
        return true;
    }

    private void Expect(char c)
    {
        if (!Peek(c))
            throw new BasicExpressionException($"Expected '{c}'.");
        _pos++;
    }

    private BasicValue ParseOr()
    {
        var left = ParseAnd();
        while (Accept("OR"))
            left = new BasicNumber((short)ToNumber(left) | (short)ToNumber(ParseAnd()));
        return left;
    }

    private BasicValue ParseAnd()
    {
        var left = ParseNot();
        while (Accept("AND"))
            left = new BasicNumber((short)ToNumber(left) & (short)ToNumber(ParseNot()));
        return left;
    }

    private BasicValue ParseNot()
    {
        if (Accept("NOT"))
            return new BasicNumber(~(short)ToNumber(ParseNot()));
        return ParseComparison();
    }

    private BasicValue ParseComparison()
    {
        var left = ParseAdditive();
        foreach (var op in new[] { "<>", "<=", ">=", "=<", "=>", "><", "=", "<", ">" })
        {
            if (!Accept(op))
                continue;

            var right = ParseAdditive();
            int compare;
            if (left is BasicString ls && right is BasicString rs)
                compare = CompareBytes(ls.Bytes, rs.Bytes);
            else
                compare = ToNumber(left).CompareTo(ToNumber(right));

            return op switch
            {
                "=" => Bool(compare == 0),
                "<>" or "><" => Bool(compare != 0),
                "<" => Bool(compare < 0),
                ">" => Bool(compare > 0),
                "<=" or "=<" => Bool(compare <= 0),
                _ => Bool(compare >= 0),
            };
        }
        return left;
    }

    private static int CompareBytes(byte[] a, byte[] b)
    {
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            if (a[i] != b[i])
                return a[i].CompareTo(b[i]);
        }
        return a.Length.CompareTo(b.Length);
    }

    private BasicValue ParseAdditive()
    {
        var left = ParseTerm();
        while (true)
        {
            if (Accept("+"))
            {
                var right = ParseTerm();
                left = left is BasicString ls && right is BasicString rs
                    ? new BasicString([.. ls.Bytes, .. rs.Bytes], 0)
                    : new BasicNumber(ToNumber(left) + ToNumber(right));
            }
            else if (Accept("-"))
            {
                left = new BasicNumber(ToNumber(left) - ToNumber(ParseTerm()));
            }
            else
            {
                return left;
            }
        }
    }

    private BasicValue ParseTerm()
    {
        var left = ParseUnary();
        while (true)
        {
            if (Accept("*"))
            {
                left = new BasicNumber(ToNumber(left) * ToNumber(ParseUnary()));
            }
            else if (Accept("/"))
            {
                var right = ToNumber(ParseUnary());
                if (right == 0)
                    throw new BasicExpressionException("Division by zero.");
                left = new BasicNumber(ToNumber(left) / right);
            }
            else
            {
                return left;
            }
        }
    }

    private BasicValue ParseUnary()
    {
        if (Accept("-"))
            return new BasicNumber(-ToNumber(ParseUnary()));
        if (Accept("+"))
            return ParseUnary();
        return ParsePower();
    }

    private BasicValue ParsePower()
    {
        var left = ParsePrimary();
        while (Accept("^"))
            left = new BasicNumber(Math.Pow(ToNumber(left), ToNumber(ParsePrimary())));
        return left;
    }

    private BasicValue ParsePrimary()
    {
        SkipSpaces();
        if (_pos >= _text.Length)
            throw new BasicExpressionException("Expected a value.");

        var c = _text[_pos];

        if (c == '(')
        {
            _pos++;
            var inner = ParseOr();
            Expect(')');
            return inner;
        }

        if (c == '"')
            return ReadString();

        if (c == '$' || c == '%' || c == '.' || char.IsAsciiDigit(c))
            return new BasicNumber(ReadNumber());

        var name = ReadName() ?? throw new BasicExpressionException($"Didn't expect '{c}'.");

        if (TryFunction(name, out var result))
            return result;

        if (Peek('('))
        {
            var array = BasicVariables.FindArray(memory, symbols, name);
            var subscripts = ReadSubscripts();
            if (array == null)
                return Default(name);
            return BasicVariables.ReadElement(memory, array.Type, ElementAddress(array, subscripts));
        }

        var variable = BasicVariables.FindVariable(memory, symbols, name);
        return variable?.Value ?? Default(name);
    }

    private static BasicValue Default(string name) => name.EndsWith('$') ? new BasicString([], 0) : new BasicNumber(0);

    private static int ElementAddress(BasicArray array, int[] subscripts)
    {
        try
        {
            return array.ElementAddress(subscripts);
        }
        catch (ArgumentException e)
        {
            throw new BasicExpressionException(e.Message);
        }
    }

    private int[] ReadSubscripts()
    {
        Expect('(');
        var result = new List<int>();
        do
        {
            var value = ToNumber(ParseOr());
            result.Add((int)Math.Floor(value));
        } while (Accept(","));
        Expect(')');
        return result.ToArray();
    }

    private string? ReadName()
    {
        SkipSpaces();
        var start = _pos;
        if (_pos >= _text.Length || !char.IsAsciiLetter(_text[_pos]))
            return null;

        while (_pos < _text.Length && char.IsAsciiLetterOrDigit(_text[_pos]))
            _pos++;
        if (_pos < _text.Length && _text[_pos] is '$' or '%')
            _pos++;

        return _text[start.._pos];
    }

    private BasicString ReadString()
    {
        _pos++; // the opening quote
        var end = _text.IndexOf('"', _pos);
        if (end == -1)
            end = _text.Length;

        var literal = "\"" + _text[_pos..end] + "\"";
        _pos = Math.Min(end + 1, _text.Length);

        var (bytes, _) = Petscii.EncodeLine(literal, charset, 0, null);
        return new BasicString(bytes[1..^1], 0);
    }

    private double ReadNumber()
    {
        var c = _text[_pos];
        if (c == '$' || c == '%')
        {
            _pos++;
            var start = _pos;
            var radix = c == '$' ? 16 : 2;
            while (_pos < _text.Length && Uri.IsHexDigit(_text[_pos]) && (radix == 16 || _text[_pos] is '0' or '1'))
                _pos++;
            if (start == _pos)
                throw new BasicExpressionException($"Expected digits after '{c}'.");
            return Convert.ToInt32(_text[start.._pos], radix);
        }

        var begin = _pos;
        while (_pos < _text.Length && (char.IsAsciiDigit(_text[_pos]) || _text[_pos] == '.'))
            _pos++;
        if (_pos < _text.Length && _text[_pos] is 'e' or 'E')
        {
            var save = _pos;
            _pos++;
            if (_pos < _text.Length && _text[_pos] is '+' or '-')
                _pos++;
            if (_pos < _text.Length && char.IsAsciiDigit(_text[_pos]))
            {
                while (_pos < _text.Length && char.IsAsciiDigit(_text[_pos]))
                    _pos++;
            }
            else
            {
                _pos = save;
            }
        }

        if (!double.TryParse(_text[begin.._pos], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new BasicExpressionException($"'{_text[begin.._pos]}' isn't a number.");
        return value;
    }

    #endregion

    #region Functions

    private bool TryFunction(string name, out BasicValue result)
    {
        result = null!;
        var upper = name.ToUpperInvariant();

        Func<BasicValue[], BasicValue>? function = upper switch
        {
            "ABS" => a => new BasicNumber(Math.Abs(Num(a, 0))),
            "INT" => a => new BasicNumber(Math.Floor(Num(a, 0))),
            "SGN" => a => new BasicNumber(Math.Sign(Num(a, 0))),
            "SQR" => a => new BasicNumber(Math.Sqrt(Num(a, 0))),
            "SIN" => a => new BasicNumber(Math.Sin(Num(a, 0))),
            "COS" => a => new BasicNumber(Math.Cos(Num(a, 0))),
            "TAN" => a => new BasicNumber(Math.Tan(Num(a, 0))),
            "ATN" => a => new BasicNumber(Math.Atan(Num(a, 0))),
            "EXP" => a => new BasicNumber(Math.Exp(Num(a, 0))),
            "LOG" => a => new BasicNumber(Math.Log(Num(a, 0))),
            "PEEK" => a => new BasicNumber(memory.ReadByte((int)Num(a, 0))),
            "LEN" => a => new BasicNumber(Str(a, 0).Length),
            "ASC" => a => Str(a, 0).Length == 0 ? throw new BasicExpressionException("ASC of an empty string.") : new BasicNumber(Str(a, 0)[0]),
            "VAL" => a => new BasicNumber(Val(Str(a, 0))),
            "CHR$" => a => new BasicString([(byte)Num(a, 0)], 0),
            "STR$" => a => Text((Num(a, 0) >= 0 ? " " : "") + BasicFloat.Format(Num(a, 0))),
            "HEX$" => a => Text(((int)Num(a, 0)).ToString((int)Num(a, 0) > 0xff ? "X4" : "X2", CultureInfo.InvariantCulture)),
            "LEFT$" => a => new BasicString(Str(a, 0).Take((int)Num(a, 1)).ToArray(), 0),
            "RIGHT$" => a => new BasicString(Str(a, 0).Skip(Math.Max(0, Str(a, 0).Length - (int)Num(a, 1))).ToArray(), 0),
            "MID$" => a => new BasicString(Str(a, 0).Skip((int)Num(a, 1) - 1).Take(a.Length > 2 ? (int)Num(a, 2) : 255).ToArray(), 0),
            _ => null
        };

        if (function == null || !Peek('('))
            return false;

        _pos++; // (
        var arguments = new List<BasicValue>();
        if (!Peek(')'))
        {
            do
            {
                arguments.Add(ParseOr());
            } while (Accept(","));
        }
        Expect(')');

        result = function(arguments.ToArray());
        return true;
    }

    private static double Num(BasicValue[] a, int i) =>
        i < a.Length ? ToNumber(a[i]) : throw new BasicExpressionException("Missing an argument.");

    private static byte[] Str(BasicValue[] a, int i) =>
        i < a.Length ? ToStringValue(a[i]).Bytes : throw new BasicExpressionException("Missing an argument.");

    private static BasicString Text(string s) => new(Encoding.ASCII.GetBytes(s), 0);

    private static double Val(byte[] bytes)
    {
        var s = Encoding.ASCII.GetString(bytes).Replace(" ", "");
        var end = 0;
        while (end < s.Length && (char.IsAsciiDigit(s[end]) || s[end] is '.' or '-' or '+' or 'E' or 'e'))
            end++;
        for (; end > 0; end--)
        {
            if (double.TryParse(s[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v;
        }
        return 0;
    }

    #endregion
}

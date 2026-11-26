using System.Text;

namespace BitMagic.X16Basic.RomTests;

/// <summary>
/// Types programs into the real ROM through EXEC and checks our tokeniser stores exactly the
/// same bytes.
/// </summary>
[TestClass]
public sealed class TokeniserRomTests
{
    /// <summary>
    /// Lines chosen to cover every keyword and the awkward cases in <c>crunch</c>.
    /// Each has to fit BASIC's 88 character input buffer.
    /// </summary>
    public static readonly string[] Corpus =
    [
        "10 PRINT \"HELLO WORLD\"",
        "20 A=1:B=A+2:C=A*B/2-1^2",
        "30 FORM=1TO10STEP2:NEXTM",
        "40 IF A>=B AND C<>D OR NOT E THEN 100",
        "50 ?A;B,C$:?:PRINT#1,X",
        "60 INPUT#1,A$:INPUT \"NAME\";N$:GET K$",
        "70 DATA 1,2,\"THREE\",FOUR PRINT,5:PRINT \"AFTER DATA\"",
        "80 REM PRINT \"THIS\" IS NOT TOKENISED: AND NEITHER IS THIS",
        "90 GOTO 10:GO TO 10:GOSUB 1000:ON X GOTO 10,20,30:ON Y GOSUB 1,2",
        "100 DEF FNA(X)=X*X+SIN(X)-COS(X)+TAN(X)+ATN(X)",
        "110 Y=FNA(2)+SGN(-1)+INT(1.5)+ABS(-2)+SQR(4)+RND(1)+LOG(1)+EXP(0)",
        "120 A$=LEFT$(\"HI\",1)+RIGHT$(\"HI\",1)+MID$(\"HI\",2,1)+STR$(1)+CHR$(65)",
        "130 X=LEN(A$)+VAL(\"12\")+ASC(\"A\")+PEEK(1)+FRE(0)+POS(0)+USR(0)",
        "140 POKE 1,2:SYS 65490:WAIT 1,2:CLR:CMD 1:OPEN 1,8,2:CLOSE 1",
        "150 VPOKE 1,$1000,2:VLOAD \"X\",8,0,0:SCREEN 128:PSET 1,2,3:LINE 1,2,3,4,5",
        "160 FRAME 1,2,3,4,5:RECT 1,2,3,4,5:CHAR 1,2,3,\"A\":MOUSE 1:COLOR 1,6",
        "170 LOCATE 1,2:KEYMAP \"DE\":BLOAD \"A\",8,1,$A000:BVLOAD \"A\",8,0,0:BANK 1",
        "180 FMINIT:FMNOTE 0,$4A:FMDRUM 0,1:FMINST 0,1:FMVIB 1,2:FMFREQ 0,440:FMVOL 0,63",
        "190 FMPAN 0,3:FMPLAY 0,\"CDE\":FMCHORD 0,\"CEG\":FMPOKE 1,2:PSGINIT:PSGNOTE 0,$4A",
        "200 PSGVOL 0,63:PSGWAV 0,1:PSGFREQ 0,440:PSGPAN 0,3:PSGPLAY 0,\"C\":PSGCHORD 0,\"C\"",
        "210 SLEEP 1:BSAVE \"A\",8,1,$A000,$A100:LINPUT A$:LINPUT#1,A$:BINPUT#1,A$,1",
        "220 TILE 1,2,3:SPRITE 0,3:SPRMEM 0,1,$1000:MOVSPR 0,10,10:OVAL 1,2,3,4,5:RING 1",
        "230 X=VPEEK(1,0)+MX+MY+MB+JOY(1)+I2CPEEK(1,2)+POINTER(A)+STRPTR(A$)+MWHEEL",
        "240 A$=HEX$(255)+BIN$(3)+RPT$(65,3):X=TDATA(1,2)+TATTR(1,2)",
        "250 PRINT TAB(5);SPC(2);:I2CPOKE 1,2,3:REBOOT:POWEROFF:MENU:REN:HELP:BANNER",
        "260 MON:DOS\"$\":OLD:GEOS:TEST:RESET:CLS:CODEX:BOOT:EDIT \"X\":EXEC $A000,1",
        "265 BASLOAD \"X\":BVERIFY \"A\",8,1,$A000",
        "270 LIST:CONT:NEW:RUN:STOP:END:RETURN:LOAD\"X\",8:SAVE\"X\",8:VERIFY\"X\",8:RESTORE",
        "280 READ A:DIM A(10),B%(2,3),C$(5):LET X=1",
        "290 PRINT \"{CLR}{RED}COLOURS{WHT}\":PRINT\"{PI}\":X={PI}",
        "300   SPACES    BEFORE   AND   AFTER   ",
        "310 PRINT\"UNCLOSED STRING",
        "320 TOTAL=1:ONE=2:STOPPED=3:GOTOX=4:IFA=5:ATN2=6:FNORD=7",
        "330 P{$CF}1,2:X{$C1}=1:G{$CF}10",
        "340 A=1E10+1.5E-3+.5:B=-1:C=$FF+%101",
        "350 PRINT 3 4 : rem lowercase rem",
        "360 print \"lower case keywords\" : goto 10",
        "370 X=1:Y=2::Z=3:",
        "380 IFX=1THENPRINT\"Y\"",
        "390 PRINT \"QUOTE \"\" INSIDE\"",
        "400 DATA \"A:B\":REM AFTER DATA",
        "410 DATA  SPACES , AND ,\"QUOTED, COMMA\"",
        "420 X=A AND B OR C:Y=NOT X:Z=X^2:W=X<Y:V=X>Y:U=X=Y",
        "430 GOSUB1000:RETURN",
        "1 0 0 0 PRINT \"SPACES IN THE NUMBER\"",
        "63999 END",
    ];

    private static void AssertSame(RomMachine machine, BasicProgram program)
    {
        Assert.IsFalse(program.HasErrors, string.Join(Environment.NewLine, program.Diagnostics));

        var expected = program.ToMemoryImage(machine.ReadWord(machine.Symbols.Txttab));
        var actual = machine.ProgramBytes();
        Assert.AreEqual(program.Lines.Count, Detokeniser.List(machine, machine.ReadWord(machine.Symbols.Txttab), machine.Keywords).Count(),
            "The ROM doesn't hold the number of lines expected; did EXEC run?");

        if (expected.AsSpan().SequenceEqual(actual))
            return;

        // find the first line that differs, to make the failure readable
        var ours = new ArrayMemory();
        ours.WriteBytes(0x801, expected);
        var romLines = Detokeniser.List(machine, 0x801, machine.Keywords).ToList();
        var ourLines = Detokeniser.List(ours, 0x801, machine.Keywords).ToList();

        var sb = new StringBuilder();
        for (var i = 0; i < Math.Max(romLines.Count, ourLines.Count); i++)
        {
            var rom = i < romLines.Count ? romLines[i] : null;
            var our = i < ourLines.Count ? ourLines[i] : null;
            if (rom?.LineNumber == our?.LineNumber && rom != null && our != null && rom.Body.AsSpan().SequenceEqual(our.Body))
                continue;

            sb.AppendLine($"ROM: {rom?.LineNumber} {(rom == null ? "" : Convert.ToHexString(rom.Body))}");
            sb.AppendLine($"Us:  {our?.LineNumber} {(our == null ? "" : Convert.ToHexString(our.Body))}");
            break;
        }
        sb.AppendLine(machine.ScreenText());
        Assert.Fail($"Program bytes differ ({actual.Length} from the ROM, {expected.Length} from us).{Environment.NewLine}{sb}");
    }

    [TestMethod]
    public void SpikeProgram()
    {
        var machine = RomMachine.Boot();
        string[] source = ["10 A=1:B=A+2", "20 GOSUB100", "30 PRINTB", "40 END", "100 A$=\"HI\":RETURN"];
        machine.Exec(source);
        AssertSame(machine, BasicTokeniser.Tokenise(string.Join('\n', source), new BasicTokeniserOptions { Keywords = machine.Keywords }));
    }

    [TestMethod]
    public void WholeCorpus()
    {
        foreach (var line in Corpus)
            Assert.IsLessThanOrEqualTo(88, Petscii.EncodeLine(line, PetsciiCase.Upper, 0, null).Bytes.Length, line);

        var machine = RomMachine.Boot();
        machine.Exec(Corpus);
        AssertSame(machine, BasicTokeniser.Tokenise(string.Join('\n', Corpus), new BasicTokeniserOptions { Keywords = machine.Keywords }));
    }

    [TestMethod]
    public void MixedCaseStrings()
    {
        string[] source = ["10 PRINT \"Hello World\"", "20 A$=\"MiXeD\":REM Not In Quotes"];
        var options = new BasicTokeniserOptions { Charset = PetsciiCase.Mixed };

        var machine = RomMachine.Boot();
        machine.Exec(source.Select(l => Petscii.EncodeLine(l, PetsciiCase.Mixed, 0, null).Bytes));
        AssertSame(machine, BasicTokeniser.Tokenise(string.Join('\n', source), options with { Keywords = machine.Keywords }));
    }

    [TestMethod]
    public void ReplacingAndDeletingLines()
    {
        string[] source = ["20 PRINT 2", "10 PRINT 1", "30 PRINT 3", "20 PRINT \"TWO\"", "30"];
        var machine = RomMachine.Boot();
        machine.Exec(source);
        AssertSame(machine, BasicTokeniser.Tokenise(string.Join('\n', source), new BasicTokeniserOptions { Keywords = machine.Keywords }));
    }

    [TestMethod]
    public void KeywordTableMatchesRom()
    {
        var machine = RomMachine.Boot();
        CollectionAssert.AreEqual(KeywordTable.Default.Main, machine.Keywords.Main);
        CollectionAssert.AreEqual(KeywordTable.Default.Extended, machine.Keywords.Extended);
        CollectionAssert.AreEqual(KeywordTable.Default.Extended2, machine.Keywords.Extended2);
        Assert.AreEqual(KeywordTable.Default.ExtendedStatementCount, machine.Keywords.ExtendedStatementCount);
    }

    [TestMethod]
    public void ListingMatchesSource()
    {
        // LIST's own output isn't easy to read back, so check ours lists the ROM's program as written
        string[] source = ["10 PRINT \"HELLO\";CHR$(13)", "20 FOR I=1 TO 10 STEP 2:VPOKE 1,I,0:NEXT"];
        var machine = RomMachine.Boot();
        machine.Exec(source);

        var listed = Detokeniser.List(machine, machine.ReadWord(machine.Symbols.Txttab), machine.Keywords).Select(l => l.ToString()).ToArray();
        CollectionAssert.AreEqual(source, listed);
    }
}

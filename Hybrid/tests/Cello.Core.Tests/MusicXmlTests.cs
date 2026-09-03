using Cello.Notation;

namespace Cello.Core.Tests;

public sealed class MusicXmlTests
{
    [Fact]
    public void SingleNoteExport_CanBeReadBack()
    {
        string xml = MusicXmlExporter.CreateSingleNoteScore(PitchResult.FromFrequency(220, 1, 0.2));

        MusicXmlScore score = MusicXmlReader.Read(xml);

        Assert.Equal("Erkannter Celloton", score.Title);
        Assert.Single(score.Tones);
        Assert.Equal(57, score.Tones[0].Pitch.MidiNote);
        Assert.Equal(1, score.MeasureCount);
    }

    [Fact]
    public void Reader_RejectsUnsupportedRoot()
    {
        FormatException exception = Assert.Throws<FormatException>(() => MusicXmlReader.Read("<score-timewise />"));

        Assert.Contains("keine unterstützte MusicXML-Partitur", exception.Message);
    }

    [Fact]
    public void Reader_PreservesNotationAndPerformanceDetails()
    {
        const string xml = """
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>Cello</part-name></score-part></part-list>
              <part id="P1"><measure number="1">
                <attributes><divisions>16</divisions><key><fifths>-2</fifths><mode>minor</mode></key></attributes>
                <direction placement="below"><direction-type><dynamics><mf/></dynamics></direction-type></direction>
                <direction placement="above"><direction-type><words>dolce</words></direction-type></direction>
                <note><pitch><step>B</step><alter>-1</alter><octave>3</octave></pitch><duration>24</duration><type>quarter</type><dot/>
                  <tie type="start"/><notations><tied type="start"/><slur type="start" number="1"/><articulations><staccato/></articulations></notations>
                  <stem>down</stem><beam number="1">begin</beam><beam number="2">begin</beam>
                </note>
                <note><pitch><step>C</step><octave>3</octave></pitch><duration>2</duration><type>32nd</type>
                  <tie type="stop"/><notations><tied type="stop"/><slur type="stop" number="1"/></notations>
                  <stem>down</stem><beam number="1">end</beam><beam number="2">end</beam>
                </note>
              </measure></part>
            </score-partwise>
            """;

        MusicXmlScore score = MusicXmlReader.Read(xml);

        Assert.Collection(score.Tones,
            first =>
            {
                Assert.Equal(NotationNoteValue.Quarter, first.NoteValue);
                Assert.Equal("B♭3", first.DisplayNoteName);
                Assert.Equal(1, first.DotCount);
                Assert.True(first.TieStarts);
                Assert.True(first.SlurStarts);
                Assert.True(first.IsStaccato);
                Assert.Equal(NotationStemDirection.Down, first.StemDirection);
                Assert.Equal(
                  [new NotationBeam(1, NotationBeamType.Begin), new NotationBeam(2, NotationBeamType.Begin)],
                  first.Beams);
            },
            second =>
            {
                Assert.Equal(NotationNoteValue.ThirtySecond, second.NoteValue);
                Assert.True(second.TieStops);
                Assert.True(second.SlurStops);
                Assert.Equal(NotationStemDirection.Down, second.StemDirection);
                Assert.Equal(
                  [new NotationBeam(1, NotationBeamType.End), new NotationBeam(2, NotationBeamType.End)],
                  second.Beams);
            });
        Assert.Contains(score.Directives, directive => directive.Kind == MusicXmlDirectiveKind.Dynamic && directive.Value == "mf");
        Assert.Contains(score.Directives, directive => directive.Kind == MusicXmlDirectiveKind.Words && directive.Value == "dolce");
        Assert.Equal(-2, score.KeyFifths);
        Assert.Equal("minor", score.KeyMode);
        Assert.Contains("2 ♭", score.KeySignatureText);
    }

      [Fact]
      public void Reader_AppliesPizzicatoAndArcoDirectionsToFollowingTones()
      {
        const string xml = """
          <score-partwise version="4.0">
            <part-list><score-part id="P1"><part-name>Cello</part-name></score-part></part-list>
            <part id="P1"><measure number="1">
            <attributes><divisions>4</divisions></attributes>
            <direction><direction-type><words>pizz.</words></direction-type><sound pizzicato="yes"/></direction>
            <note><pitch><step>C</step><octave>3</octave></pitch><duration>4</duration><type>quarter</type></note>
            <note><pitch><step>D</step><octave>3</octave></pitch><duration>4</duration><type>quarter</type></note>
            <direction><direction-type><words>Arco</words></direction-type><sound pizzicato="no"/></direction>
            <note><pitch><step>E</step><octave>3</octave></pitch><duration>4</duration><type>quarter</type></note>
            </measure></part>
          </score-partwise>
          """;

        MusicXmlScore score = MusicXmlReader.Read(xml);

        Assert.True(score.Tones[0].IsPizzicato);
        Assert.True(score.Tones[1].IsPizzicato);
        Assert.False(score.Tones[2].IsPizzicato);
        Assert.Contains(score.Directives, directive =>
          directive.Kind == MusicXmlDirectiveKind.Pizzicato && directive.Value == "yes");
        Assert.Contains(score.Directives, directive =>
          directive.Kind == MusicXmlDirectiveKind.Pizzicato && directive.Value == "no");
      }

    [Fact]
    public void Reader_PreservesTripletChords()
    {
        const string xml = """
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>Cello</part-name></score-part></part-list>
              <part id="P1"><measure number="1">
                <attributes><divisions>12</divisions></attributes>
                <note><pitch><step>G</step><octave>2</octave></pitch><duration>4</duration><type>eighth</type>
                  <time-modification><actual-notes>3</actual-notes><normal-notes>2</normal-notes></time-modification>
                  <notations><tuplet type="start"/></notations></note>
                <note><chord/><pitch><step>D</step><octave>3</octave></pitch><duration>4</duration><type>eighth</type>
                  <time-modification><actual-notes>3</actual-notes><normal-notes>2</normal-notes></time-modification></note>
                <note><pitch><step>A</step><octave>2</octave></pitch><duration>4</duration><type>eighth</type>
                  <time-modification><actual-notes>3</actual-notes><normal-notes>2</normal-notes></time-modification>
                  <notations><tuplet type="stop"/></notations></note>
              </measure></part>
            </score-partwise>
            """;

        MusicXmlScore score = MusicXmlReader.Read(xml);

        Assert.Equal(3, score.Tones.Count);
        Assert.False(score.Tones[0].IsChordContinuation);
        Assert.True(score.Tones[1].IsChordContinuation);
        Assert.False(score.Tones[2].IsChordContinuation);
        Assert.Equal(3, score.Tones[0].TupletActualNotes);
        Assert.Equal(2, score.Tones[0].TupletNormalNotes);
        Assert.True(score.Tones[0].TupletStarts);
        Assert.True(score.Tones[2].TupletStops);
    }
}

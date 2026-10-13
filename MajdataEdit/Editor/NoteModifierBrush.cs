using System.Text;
using MajdataCore;

namespace MajdataEdit.Editor;

internal static class NoteModifierBrush
{
    public static string Transform(string selected, char modifier)
    {
        var result = new StringBuilder();
        var start = 0;
        void Flush(int end) { result.Append(Change(selected.Substring(start,end-start),modifier)); }
        for(var i=0;i<selected.Length;i++)
        {
            if(selected[i]=='[')
            {
                var depth=1;
                while(++i<selected.Length && depth>0) { if(selected[i]=='[') depth++;else if(selected[i]==']') depth--; }
                i--; continue;
            }
            if(selected[i]=='<' && AlphaCommandBoundary.TryGetCommand(selected,i,out var close))
            { Flush(i);result.Append(selected,i,close-i+1);i=close;start=i+1;continue; }
            if(selected[i] is '{' or '(')
            {
                var closeMarker=selected.IndexOf(selected[i]=='{'?'}':')',i+1);
                if(closeMarker<0) continue;
                Flush(i);result.Append(selected,i,closeMarker-i+1);i=closeMarker;start=i+1;continue;
            }
            if(selected[i]=='|' && i+1<selected.Length && selected[i+1]=='|')
            {
                Flush(i);var end=selected.IndexOf('\n',i);if(end<0) end=selected.Length;
                result.Append(selected,i,end-i);i=end-1;start=end;continue;
            }
            // A selection can start at the closing half of a BPM/division token.
            // Keep that delimiter without including it in the following note.
            if(selected[i] is ',' or '/' or '*' or '`' or '\r' or '\n' or '}' or ')')
            { Flush(i);result.Append(selected[i]);start=i+1; }
        }
        Flush(selected.Length);return result.ToString();
    }

    private static string Change(string raw,char modifier)
    {
        var text=raw.Trim();if(text.Length==0) return raw;
        if(text.Length==2 && text.All(c=>c is >= '1' and <= '8'))
            return raw.Replace(text,$"{text[0]}{modifier}/{text[1]}{modifier}");
        var branch=SlidePathParser.ContainsSlideShape(text[..1]);
        var candidate=branch?"1"+text:text;
        if(!NoteExpressionParser.TryParse(candidate,out var note,out _,forPreview:true) || note.kind==NoteExpressionKind.NoiseZone) return raw;
        var additions=new List<int>();
        var flag=modifier=='b'?NoteModifierFlags.Break:NoteModifierFlags.Ex;
        if(!branch && !note.modifiers.HasHead(flag) && SlidePathParser.TryReadPosition(text,0,out _,out var offset))
        {
            var holdAt = text.IndexOf('h');
            additions.Add(modifier == 'b' && note.kind is (NoteExpressionKind.Hold or NoteExpressionKind.TouchHold) && holdAt >= offset
                ? holdAt + 1 : offset);
        }
        if(modifier=='b' && note.kind==NoteExpressionKind.Slide)
        {
            var search=0;
            foreach(var segment in note.path.segments)
            {
                if(segment.duration.Length==0) continue;
                var at=FindDuration(text,segment.duration,search);
                if(at<0) continue;
                search=at+segment.duration.Length;
                if(!segment.modifiers.Contains('b')) additions.Add(at);
            }
            if(note.path.segments.All(s=>s.duration.Length==0) && !note.modifiers.HasSlide(flag))
            {
                var suffix=text.IndexOf("~[",StringComparison.Ordinal);
                additions.Add(suffix<0?text.Length:suffix);
            }
        }
        var changed=new StringBuilder(text);
        foreach(var at in additions.Distinct().OrderByDescending(x=>x)) changed.Insert(at,modifier);
        return raw[..(raw.Length-raw.TrimStart().Length)]+changed+raw[(raw.TrimEnd().Length)..];
    }

    private static int FindDuration(string text,string token,int offset)
    {
        for(var i=offset;i<text.Length;i++)
        {
            if(text[i]=='~' && i+1<text.Length && text[i+1]=='[')
            {
                var depth=0;do { if(text[i]=='[') depth++;else if(text[i]==']') depth--; i++; }
                while(i<text.Length && (depth>0 || text[i-1]=='~'));
                i--;continue;
            }
            if(text[i]=='[' && text.AsSpan(i).StartsWith(token.AsSpan(),StringComparison.Ordinal)) return i;
        }
        return -1;
    }
}

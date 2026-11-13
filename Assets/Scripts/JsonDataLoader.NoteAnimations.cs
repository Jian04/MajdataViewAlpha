using System;
using System.Collections.Generic;
using System.Linq;
using MajdataCore;
using UnityEngine;

public partial class JsonDataLoader
{
    private bool HasNoteAnimations;
    internal bool HasSizeAnimations { get; private set; }
    internal bool HasAlphaAnimations { get; private set; }
    internal bool HasColorAnimations { get; private set; }
    internal bool HasAngleAnimations { get; private set; }
    private readonly Dictionary<string, List<(double time, AngleChange value)>> angleTimeline = new();
    private readonly Dictionary<(int Stream,string Type,bool Live),string> animationKeys = new();
    private readonly Dictionary<SimaiTimingPoint,(string Key,double Appearance,float Speed)> noiseMotion = new();

    private void BuildNoteAnimations()
    {
        noiseMotion.Clear();
        animationKeys.Clear();
        HasSizeAnimations = loadedData.sizeTable.Any(value => value.transition != null);
        HasAlphaAnimations = loadedData.alphaTable.Any(value => value.transition != null);
        HasColorAnimations = loadedData.colorTable.Any(value => value.transition != null);
        HasAngleAnimations = loadedData.angleTable.Count > 0;
        HasNoteAnimations = HasSizeAnimations || HasAlphaAnimations || HasColorAnimations || HasAngleAnimations ||
            loadedData.hsTable.Any(value => value.transition != null) ||
            loadedData.spawnTable.Any(value => value.transition != null) ||
            loadedData.destroyTable.Any(value => value.transition != null);
        angleTimeline.Clear();
        foreach (var value in loadedData.angleTable)
        {
            var key = VisualStateKey(value.streamIndex, value.noteType, value.live);
            if (!angleTimeline.TryGetValue(key, out var list)) angleTimeline[key] = list = new();
            list.Add((value.time, value));
        }
        foreach (var list in angleTimeline.Values)
            list.Sort((a,b) => a.time != b.time ? a.time.CompareTo(b.time) :
                a.value.sourcePosition.CompareTo(b.value.sourcePosition));
    }

    private (string Key,double Appearance,float Speed) GetNoiseMotion(SimaiTimingPoint timing)
    {
        if(noiseMotion.TryGetValue(timing,out var motion)) return motion;
        SpeedChange Lookup(string type)
        {
            if(!_hSpeedTimeline.TryGetValue(StateKey(timing.streamIndex,type),out var list)) return null;
            var index=FindTimelineIndex(list,timing.time);
            return index>=0 && !list[index].value.reset ? list[index].value : null;
        }
        var source=Lookup("blockarea") ?? Lookup("");
        var key=SvController.MakeCurveKey(timing.streamIndex,"blockarea");
        var speed=touchSpeed*(source?.transition?.from ?? source?.multiplier ?? timing.HSpeed);
        var appearance=timing.time-SvController.GetTouchWarningLead(timing.time,speed,key);
        if(source?.transition != null)
        {
            key=SvController.CreateBirthSpeedCurve(key,timing.time,
                AlphaVisualTiming.GetTouchMotionDuration(touchSpeed),source.transition,timing.time+.15d,ref appearance);
            speed=touchSpeed;
        }
        return noiseMotion[timing]=(key,appearance,speed);
    }

    private T AnimationState<T>(Dictionary<string,List<(double time,T value)>> timeline,
        NoteDrop note,Func<T,bool> reset,bool includeGlobal = true) where T : class
    {
        T Lookup(string type)
        {
            if (!timeline.TryGetValue(StateKey(note.VisualStreamIndex,type),out var list)) return null;
            var index = FindTimelineIndex(list,note.VisualStateTime);
            return index >= 0 && !reset(list[index].value) ? list[index].value : null;
        }
        return (note.IsVisualMine ? Lookup("mine") : null) ??
            (note.IsVisualBreak ? Lookup("break") : null) ??
            (note.isEachInStream ? Lookup("each") : null) ?? Lookup(note.VisualNoteType) ??
            (includeGlobal ? Lookup("") : null);
    }

    internal NoteValueTransition FindBirthHs(NoteDrop note) =>
        AnimationState(_hSpeedTimeline,note,value=>value.reset,note.VisualNoteType != "slide")?.transition;
    internal NoteValueTransition FindBirthSpawn(NoteDrop note) =>
        IsRingAnimation(note) ? AnimationState(_spawnTimeline,note,value=>value.reset)?.transition : null;
    internal NoteValueTransition FindBirthDestroy(NoteDrop note) =>
        IsRingAnimation(note) ? AnimationState(_destroyTimeline,note,value=>value.reset)?.transition : null;
    private static bool IsRingAnimation(NoteDrop note) => note.VisualNoteType is "tap" or "hold" or "star";

    internal double GetAnimationAppearance(NoteDrop note)
    {
        if (note.VisualNoteType is "touch" or "touchhold")
            return note.VisualStateTime - SvController.GetTouchWarningLead(note.VisualStateTime,note.speed,note.scrollType);
        if (note is SlideDrop slide) return note.VisualStateTime + slide.GetAppearanceStartOffset();
        if (note is WifiDrop wifi) return note.VisualStateTime + wifi.GetAppearanceStartOffset();
        if (note is TouchSlideDrop touchSlide) return note.VisualStateTime + touchSlide.GetAppearanceStartOffset();
        return SvController.GetRingAppearanceTime(note.VisualStateTime,note.speed,
            note.spawnRadius,note.destroyRadius,note.scrollType);
    }

    private (T Value,bool Live) AnimatedVisual<T>(Dictionary<string,List<(double time,T value)>> timeline,
        NoteDrop note,string type,double now,Func<T,bool> reset,bool includeGlobal) where T:class
    {
        T Lookup(string target,bool live)
        {
            if (!timeline.TryGetValue(AnimationKey(note.VisualStreamIndex,target,live),out var list)) return null;
            var index = FindTimelineIndex(list,live ? now : note.VisualStateTime);
            return index >= 0 && !reset(list[index].value) ? list[index].value : null;
        }
        T Typed(bool live) => (note.IsVisualMine ? Lookup("mine",live) : null) ??
            (note.IsVisualBreak ? Lookup("break",live) : null) ??
            (note.isEachInStream ? Lookup("each",live) : null) ?? Lookup(type,live);
        var value = Typed(true);
        if(value != null) return (value,true);
        value = Typed(false);
        if(value != null) return (value,false);
        if(!includeGlobal) return (null,false);
        value = Lookup("",true);
        return value != null ? (value,true) : (Lookup("",false),false);
    }

    private string AnimationKey(int stream,string type,bool live)
    {
        var key=(stream,type,live);
        if(!animationKeys.TryGetValue(key,out var value))
            animationKeys[key]=value=VisualStateKey(stream,type,live);
        return value;
    }

    internal Vector2? AnimatedSize(NoteDrop note,double now,double appearance,bool guide)
    {
        var type = guide ? note.LiveGuideStarVisualType : note.VisualNoteType;
        var (value,live) = AnimatedVisual(_sizeTimeline,note,type,now,item=>item.reset,type is not ("slide" or "blockarea"));
        if(value == null) return null;
        if(value.transition != null)
        {
            var elapsed = now - (live ? value.time : appearance);
            return new Vector2(value.transition.Evaluate(elapsed),value.transition.EvaluateY(elapsed));
        }
        return new Vector2(value.scaleX == 0 ? value.scale : value.scaleX,
            value.scaleY == 0 ? value.scale : value.scaleY);
    }

    private Color? EvaluateNoteColor(ColorChange value,double elapsed)
    {
        if(value==null) return null;
        if(value.transition==null)
            return parsedNoteColors.TryGetValue(value,out var color) ? color : null;
        var rgb=value.transition.EvaluateColor(elapsed);
        var strength = value.transition.fromUntinted ? Mathf.Clamp01(value.transition.Progress(elapsed)) : 1f;
        return strength == 0f ? (Color?)null : new Color(rgb.R, rgb.G, rgb.B, strength);
    }

    internal Color? AnimatedColor(NoteDrop note,double now,double appearance,bool guide)
    {
        var type=guide ? note.LiveGuideStarVisualType : note.VisualNoteType;
        var value=FindColorState(type,note.VisualStateTime,note.VisualStreamIndex,
            note.IsVisualBreak,note.isEachInStream,now,isMine:note.IsVisualMine);
        return EvaluateNoteColor(value,now-(value?.live==true ? value.time : appearance));
    }

    internal float? AnimatedAlpha(NoteDrop note,double now,double appearance,bool guide)
    {
        var type = guide ? note.LiveGuideStarVisualType : note.VisualNoteType;
        var (value,live) = AnimatedVisual(_alphaTimeline,note,type,now,item=>item.reset,type != "blockarea");
        return value == null ? null : Mathf.Clamp01(value.transition?.Evaluate(
            now - (live ? value.time : appearance)) ?? value.alpha);
    }

    internal float AnimatedAngle(NoteDrop note,double now,double appearance,bool guide)
    {
        var type = guide ? note.LiveGuideStarVisualType : note.VisualNoteType;
        var (value,live) = AnimatedVisual(angleTimeline,note,type,now,item=>item.reset,
            type is not ("slide" or "slidestar" or "blockarea"));
        return value == null ? 0f : value.transition?.Evaluate(now - (live ? value.time : appearance)) ?? value.angle;
    }
}

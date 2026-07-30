using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ArknoNights.Battle.Core;
namespace ArknoNights.Battle.Presentation
{
public enum UnitPresentationAction { Idle, Move, Skill, Attack, Death }
public interface IBattleUnitPresentationTimeline
{
string UnitId{get;} string TypeId{get;} string PlayerId{get;} BattleSide Side{get;} int EliteLevel{get;}
UnitPresentationSample Sample(double tick);
bool TryGetMovementDirection(double tick,out FixedPosition from,out FixedPosition to);
}
public interface IBattlePresentationTimeline
{
string BattleId{get;} int EndTick{get;} IReadOnlyList<IBattleUnitPresentationTimeline> TimelineUnits{get;}
}
public sealed class BattlePresentationCompressionMetrics { internal BattlePresentationCompressionMetrics(int originalMoveCount,int positionKeyCount,double maximumErrorUnits){OriginalMoveCount=originalMoveCount;PositionKeyCount=positionKeyCount;CompressionRatio=originalMoveCount==0?1d:(double)positionKeyCount/originalMoveCount;MaximumErrorUnits=maximumErrorUnits;} public int OriginalMoveCount{get;} public int PositionKeyCount{get;} public double CompressionRatio{get;} public double MaximumErrorUnits{get;} }
public readonly struct PresentationPosition { public PresentationPosition(double x,double y){XUnits=x;YUnits=y;} public double XUnits{get;} public double YUnits{get;} }
public readonly struct UnitPresentationSample { internal UnitPresentationSample(bool s,bool a,bool exited,PresentationPosition p,int f,UnitPresentationAction action,int start,int seq,float mult,string key,string state,int max,int hp,int shield):this(s,a,exited,p,f,action,start,seq,mult,key,state,max,hp,shield,null){} internal UnitPresentationSample(bool s,bool a,bool exited,PresentationPosition p,int f,UnitPresentationAction action,int start,int seq,float mult,string key,string state,int max,int hp,int shield,BattleUnitAttributesSnapshot attributes){HasSpawned=s;IsAlive=a;HasExitedBattle=exited;ShouldDisplay=s&&a&&!exited;Position=p;HorizontalFacing=f;Action=action;ActionStartTick=start;ActionSequence=seq;AnimationSpeedMultiplier=mult;AnimationKey=key??string.Empty;PresentationStateTag=state??string.Empty;MaxHitPoints=max;CurrentHitPoints=hp;CurrentShield=shield;Attributes=attributes;} public bool HasSpawned{get;} public bool IsAlive{get;} public bool HasExitedBattle{get;} public bool ShouldDisplay{get;} public PresentationPosition Position{get;} public int HorizontalFacing{get;} public UnitPresentationAction Action{get;} public int ActionStartTick{get;} public int ActionSequence{get;} public float AnimationSpeedMultiplier{get;} public float AttackAnimationSpeedMultiplier=>AnimationSpeedMultiplier; public string AnimationKey{get;} public string PresentationStateTag{get;} public int MaxHitPoints{get;} public int CurrentHitPoints{get;} public int CurrentShield{get;} public BattleUnitAttributesSnapshot Attributes{get;} }
public sealed class UnitPresentationTrack : IBattleUnitPresentationTimeline
{
internal readonly struct PositionSegment { public PositionSegment(int s,int e,FixedPosition f,FixedPosition t){Start=s;End=e;From=f;To=t;} public int Start{get;} public int End{get;} public FixedPosition From{get;} public FixedPosition To{get;} }
internal readonly struct HpKey { public HpKey(int t,int s,int v){Tick=t;Sequence=s;Value=v;} public int Tick{get;} public int Sequence{get;} public int Value{get;} }
internal readonly struct Attack { public Attack(int t,int s,int d,float m){Tick=t;Sequence=s;Duration=d;Multiplier=m;} public int Tick{get;} public int Sequence{get;} public int Duration{get;} public float Multiplier{get;} }
internal readonly struct Skill { public Skill(int t,int s,int d,string key){Tick=t;Sequence=s;Duration=d;Key=key??string.Empty;} public int Tick{get;} public int Sequence{get;} public int Duration{get;} public string Key{get;} }
internal readonly struct State { public State(int t,int s,string tag){Tick=t;Sequence=s;Tag=tag??string.Empty;} public int Tick{get;} public int Sequence{get;} public string Tag{get;} }
internal readonly struct AttributeKey { public AttributeKey(int t,int s,BattleUnitAttributesSnapshot value){Tick=t;Sequence=s;Value=value;} public int Tick{get;} public int Sequence{get;} public BattleUnitAttributesSnapshot Value{get;} }
private readonly IReadOnlyList<PositionSegment> positions; private readonly IReadOnlyList<HpKey> hp; private readonly IReadOnlyList<Attack> attacks; private readonly IReadOnlyList<Skill> skills; private readonly IReadOnlyList<State> states; private readonly IReadOnlyList<AttributeKey> attributes; private readonly int endTick;
internal UnitPresentationTrack(BattleUnitInstanceSnapshot x,int spawn,int end,int? death,int? exit,IEnumerable<PositionSegment> p,IEnumerable<HpKey> h,IEnumerable<Attack> a,IEnumerable<Skill> s,IEnumerable<State> st,IEnumerable<AttributeKey> attributeKeys){UnitId=x.UnitId;TypeId=x.TypeId;PlayerId=x.PlayerId;Side=x.Side;IsDynamicallyGenerated=x.IsDynamicallyGenerated;InitialPosition=x.Position;EliteLevel=x.EliteLevel;SpawnTick=spawn;ActivationTick=x.ActivationTick;DeathTick=death;ExitTick=exit;MaxHitPoints=x.MaxHitPoints;CurrentHitPoints=x.CurrentHitPoints;CurrentShield=x.CurrentShield;endTick=end;positions=new ReadOnlyCollection<PositionSegment>(p.ToArray());hp=new ReadOnlyCollection<HpKey>(h.ToArray());attacks=new ReadOnlyCollection<Attack>(a.ToArray());skills=new ReadOnlyCollection<Skill>(s.ToArray());states=new ReadOnlyCollection<State>(st.ToArray());attributes=new ReadOnlyCollection<AttributeKey>(new[]{new AttributeKey(spawn,0,x.Attributes)}.Concat(attributeKeys??Enumerable.Empty<AttributeKey>()).OrderBy(item=>item.Tick).ThenBy(item=>item.Sequence).ToArray());}
public string UnitId{get;} public string TypeId{get;} public string PlayerId{get;} public BattleSide Side{get;} public bool IsDynamicallyGenerated{get;} public FixedPosition InitialPosition{get;} public int EliteLevel{get;} public int SpawnTick{get;} public int ActivationTick{get;} public int? DeathTick{get;} public int? ExitTick{get;} public int MaxHitPoints{get;} public int CurrentHitPoints{get;} public int CurrentShield{get;}
public UnitPresentationSample Sample(double tick)
{
if(tick<SpawnTick)return new UnitPresentationSample(false,false,false,new PresentationPosition(0,0),1,UnitPresentationAction.Idle,SpawnTick,0,1,string.Empty,string.Empty,MaxHitPoints,0,CurrentShield,attributes[0].Value);
tick=Math.Min(tick,endTick);
var p=positions[0];
var foundPosition=false;
var bestPositionStart=int.MinValue;
for(var i=0;i<positions.Count;i++){var candidate=positions[i];if(tick>=candidate.Start&&tick<=candidate.End&&(!foundPosition||candidate.Start>bestPositionStart)){p=candidate;bestPositionStart=candidate.Start;foundPosition=true;}}
if(!foundPosition){var bestPositionEnd=int.MinValue;for(var i=0;i<positions.Count;i++){var candidate=positions[i];if(candidate.End<=tick&&candidate.End>bestPositionEnd){p=candidate;bestPositionEnd=candidate.End;}}}
var d=Math.Max(1,p.End-p.Start);
var r=Math.Max(0,Math.Min(1,(tick-p.Start)/d));
var pos=new PresentationPosition((p.From.XUnits+(p.To.XUnits-p.From.XUnits)*r)/100d,(p.From.YUnits+(p.To.YUnits-p.From.YUnits)*r)/100d);
var alive=!DeathTick.HasValue||tick<DeathTick.Value;
var exited=ExitTick.HasValue&&tick>=ExitTick.Value;
var action=UnitPresentationAction.Idle;
var start=SpawnTick;
var seq=0;
var mult=1f;
var key=string.Empty;
if(!alive){action=UnitPresentationAction.Death;start=DeathTick.Value;}
else if(!exited)
{
var at=default(Attack);
for(var i=attacks.Count-1;i>=0;i--){var candidate=attacks[i];if(tick>=candidate.Tick&&tick<=candidate.Tick+candidate.Duration){at=candidate;break;}}
if(at.Duration>0){action=UnitPresentationAction.Attack;start=at.Tick;seq=at.Sequence;mult=at.Multiplier;}
else
{
var skill=default(Skill);
for(var i=skills.Count-1;i>=0;i--){var candidate=skills[i];if(tick>=candidate.Tick&&tick<=candidate.Tick+candidate.Duration){skill=candidate;break;}}
if(skill.Duration>0){action=UnitPresentationAction.Skill;start=skill.Tick;seq=skill.Sequence;mult=2f;key=skill.Key;}
else if(p.End>p.Start&&tick>=p.Start&&tick<=p.End)action=UnitPresentationAction.Move;
}
}
var state=string.Empty;
for(var i=states.Count-1;i>=0;i--){if(states[i].Tick<=tick){state=states[i].Tag;break;}}
var current=hp[0].Value;
for(var i=hp.Count-1;i>=0;i--){if(hp[i].Tick<=tick){current=hp[i].Value;break;}}
var facing=1;
for(var i=positions.Count-1;i>=0;i--){var candidate=positions[i];if(candidate.Start<=tick&&candidate.To.XUnits!=candidate.From.XUnits){facing=candidate.To.XUnits<candidate.From.XUnits?-1:1;break;}}
var currentAttributes=attributes[0].Value;
for(var i=attributes.Count-1;i>=0;i--){if(attributes[i].Tick<=tick){currentAttributes=attributes[i].Value;break;}}
return new UnitPresentationSample(true,alive,exited,pos,facing,action,start,seq,mult,key,state,MaxHitPoints,current,CurrentShield,currentAttributes);
}
internal FixedPosition FinalPosition=>positions.Last().To; internal int FinalHitPoints=>hp.Last().Value; internal bool FinalAlive=>!DeathTick.HasValue;
public bool TryGetMovementDirection(double tick,out FixedPosition from,out FixedPosition to)
{
for(var i=positions.Count-1;i>=0;i--)
{
var p=positions[i];
if(tick<p.Start||tick>p.End)continue;
from=p.From;
to=p.To;
return p.End>p.Start;
}
from=default(FixedPosition);
to=default(FixedPosition);
return false;
}
}
public sealed class BattlePresentationTrack : IBattlePresentationTimeline
{
private readonly IReadOnlyList<UnitPresentationTrack> units; private readonly Dictionary<string,UnitPresentationTrack> lookup;
internal BattlePresentationTrack(BattleRunResult r,IEnumerable<UnitPresentationTrack> u,string events,BattlePresentationCompressionMetrics metrics){BattleId=r.BattleId;HomePlayerId=r.HomePlayerId;AwayPlayerId=r.AwayPlayerId;EndTick=r.CompletedTicks;Winner=r.Winner;Outcome=r.Outcome;StopReason=r.StopReason;units=new ReadOnlyCollection<UnitPresentationTrack>(u.ToArray());lookup=units.ToDictionary(x=>x.UnitId,StringComparer.Ordinal);SourceInputDigest=Digest(r.InputCanonicalSummary);SourceEventDigest=events;SourceResultDigest=Digest(r.StableSummary);CompressionMetrics=metrics??new BattlePresentationCompressionMetrics(0,0,0d);StableSummary=Digest(BattleId+SourceInputDigest+events+SourceResultDigest+CompressionMetrics.OriginalMoveCount+":"+CompressionMetrics.PositionKeyCount+":"+CompressionMetrics.CompressionRatio.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+":"+CompressionMetrics.MaximumErrorUnits.ToString("R",System.Globalization.CultureInfo.InvariantCulture));}
public string BattleId{get;} public string HomePlayerId{get;} public string AwayPlayerId{get;} public int EndTick{get;} public BattleSide? Winner{get;} public BattleOutcome Outcome{get;} public BattleStopReason StopReason{get;} public IReadOnlyList<UnitPresentationTrack> Units=>units; public IReadOnlyList<IBattleUnitPresentationTimeline> TimelineUnits=>new ReadOnlyCollection<IBattleUnitPresentationTimeline>(units.Cast<IBattleUnitPresentationTimeline>().ToArray()); public string SourceInputDigest{get;} public string SourceEventDigest{get;} public string SourceResultDigest{get;} public BattlePresentationCompressionMetrics CompressionMetrics{get;} public string StableSummary{get;} public bool TryGetUnit(string id,out UnitPresentationTrack t)=>lookup.TryGetValue(id??string.Empty,out t); internal static string Digest(string s){unchecked{uint h=2166136261;foreach(var c in s??string.Empty){h^=c;h*=16777619;}return h.ToString("X8");}}
}
}

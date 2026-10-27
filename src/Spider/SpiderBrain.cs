using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Webi.Assistant;
using Webi.World;

namespace Webi.Spider;

/// <summary>Decides what the spider does: wander, jump between windows, drop on silk, sleep, react to the cursor.</summary>
public sealed class SpiderBrain
{
    readonly Spider _spider;
    readonly SurfaceMap _map;
    readonly RandomNumberGenerator _rng = new();

    float _decisionTimer = 1.5f;
    float _faceTimer;
    Vector2 _lastCursor;
    float _holdTimer;
    float _waveCooldown;
    readonly Queue<Action> _demo = new();
    float _demoTimer;

    public bool Asleep { get; private set; }
    readonly Queue<double> _pokes = new();

    /// <summary>Webs it can weave and hang from (set by Main).</summary>
    public WebLayer? Webs { get; set; }
    public bool AutoWebs { get; set; } = true;
    public int MaxWebs { get; set; } = 5;

    /// <summary>In a meeting / streaming: alerts wait in the queue and the giant web is off.</summary>
    public bool Quiet { get; set; }

    /// <summary>Asks Main to show the huge shout bubble (Webi is in place).</summary>
    public event Action<Notice>? Shout;
    /// <summary>Work area (monitor) containing a point, set by Main.</summary>
    public Func<Vector2, Rect2>? WorkAreaAt { get; set; }

    // Alerts and the giant web both start by going to the middle of the screen: jump up to the ceiling,
    // then lower down on silk.
    enum Stage { None, ToCenter, Shouting, Weaving, InGiantWeb }
    Stage _stage;
    bool _forGiant;
    Vector2 _center;
    Rect2 _giantArea;
    float _stageTimer;
    int _stageAttempts;
    Notice _notice;
    readonly Queue<Notice> _notices = new();
    bool _giantHintShown;

    enum WebTask { None, GoBuild, Building, GoHang }

    /// <summary>Crawling speeds while weaving (screen px/s).</summary>
    const float CornerWeaveSpeed = 320, GiantWeaveSpeed = 1100;
    float _weaveDistance;
    WebTask _task;
    string _taskKey = "";
    int _taskAttempts;
    float _taskTimer, _webHangTimer;

    /// <summary>Short line to show in the speech bubble (text, seconds).</summary>
    public event Action<string, float>? Say;

    /// <summary>Flies to hunt (set by Main).</summary>
    public BugLayer? Bugs { get; set; }
    /// <summary>A fly was caught and eaten.</summary>
    public event Action? FlyEaten;
    /// <summary>Friendship level (unlocks click lines and the hug), set by Main.</summary>
    public Func<int>? FriendLevel { get; set; }

    // Hunting a fly or the resting cursor: walk closer, stalk (crouched, wiggling), pounce.
    enum Hunt { None, Approach, Stalk, Pounce }
    Hunt _hunt;
    Fly? _prey;
    bool _preyCursor, _preyNotices, _caught, _groomOnLanding;
    Vector2 _stalkCursor;
    float _huntTimer, _huntCooldown = 20, _cursorHuntCooldown = 60, _cursorStill;
    int _huntSteps;
    /// <summary>A fly stuck in one of the webs, and the time left before eating it once there.</summary>
    Fly? _webMeal;
    /// <summary>Stuck flies already gone after (announced once) and how many trips found no way there.</summary>
    readonly Dictionary<Fly, int> _mealFails = new();
    const int MaxMealFails = 3;
    float _eatTimer;

    float _tripTimer, _peekCooldown = 60, _carriedCooldown;
    bool _tripLine, _peeking, _yawned, _summonPending, _summonSay;
    Vector2 _summonAt;

    public SpiderBrain(Spider spider, SurfaceMap map)
    {
        _spider = spider;
        _map = map;
        _spider.EdgeReached += OnEdge;
        _spider.Landed += OnLanded;
        if (OS.HasEnvironment("WEBI_DEMO")) QueueDemo();
    }

    /// <summary>Debug (WEBI_DEMO=1): plays every animation in a fixed order instead of the normal behaviour.</summary>
    void QueueDemo()
    {
        if (OS.GetEnvironment("WEBI_DEMO") == "web") _demo.Enqueue(() => StartWeb());
        _demo.Enqueue(() => _spider.PlayGesture(Gesture.Wave, 1));
        _demo.Enqueue(() => _spider.PlayGesture(Gesture.Groom));
        _demo.Enqueue(() => _spider.PlayGesture(Gesture.Tap, -1));
        _demo.Enqueue(() => _spider.PlayGesture(Gesture.Stretch));
        _demo.Enqueue(() => _spider.PlayGesture(Gesture.Dance));
        _demo.Enqueue(() => _spider.PlayGesture(Gesture.Stalk, 1));
        _demo.Enqueue(() => _spider.PlayGesture(Gesture.Yawn));
        _demo.Enqueue(() => _spider.PlayGesture(Gesture.Hug));
        _demo.Enqueue(() => _spider.PlayGesture(Gesture.Trip, 1));
        _demo.Enqueue(() => _spider.PlayGesture(Gesture.Peek));
        _demo.Enqueue(() => _spider.Walk(160, -1));
        _demo.Enqueue(() => _spider.Walk(260, 1, 64));
        _demo.Enqueue(() => _spider.JumpTo(_spider.Contact + new Vector2(-260, 0)));
        _demo.Enqueue(() => _spider.JumpTo(_spider.Contact));
        _demo.Enqueue(() => _spider.Walk(3000, 1, 40));
    }

    bool TickDemo(float dt)
    {
        if (!OS.HasEnvironment("WEBI_DEMO")) return false;
        if (TickWebTask(dt) || _spider.State == Mode.Hanging) return true;
        if (_spider.State != Mode.Grounded || _spider.IsWalking || _spider.CurrentGesture != Gesture.None) return true;
        _demoTimer -= dt;
        if (_demoTimer > 0 || _demo.Count == 0) return true;
        _demoTimer = 0.6f;
        _demo.Dequeue()();
        return true;
    }

    public void Tick(float dt, Vector2 cursor, double idleSeconds, int sleepAfterSeconds)
    {
        var cursorVel = (cursor - _lastCursor) / Mathf.Max(dt, 0.001f);
        _lastCursor = cursor;
        float cursorDist = cursor.DistanceTo(_spider.BodyCenter);
        _cursorStill = cursorVel.Length() < 20 ? _cursorStill + dt : 0;
        _huntCooldown -= dt;
        _cursorHuntCooldown -= dt;
        _peekCooldown -= dt;
        _carriedCooldown -= dt;

        // The eyes always follow the mouse (except while asleep, below).
        _spider.LookTarget = cursor;
        if (TickStage(dt, idleSeconds)) return;

        // ---- a yawn a few seconds before dozing off
        if (idleSeconds < 1) _yawned = false;
        else if (!Asleep && !_yawned && idleSeconds > sleepAfterSeconds - 6 && _spider.State == Mode.Grounded
                 && !_spider.IsWalking && _spider.CurrentGesture == Gesture.None && _task == WebTask.None && _hunt == Hunt.None)
        {
            _yawned = true;
            _spider.PlayGesture(Gesture.Yawn);
            Say?.Invoke(Lines.Pick(Lines.Yawn), 2.5f);
        }

        // ---- sleep
        if (!Asleep && idleSeconds > sleepAfterSeconds && _spider.State == Mode.Grounded)
        {
            Asleep = true;
            _spider.StopWalking();
            _spider.Expression = Face.Sleepy;
            _faceTimer = 1.5f;
        }
        if (Asleep)
        {
            if (idleSeconds < 1)
            {
                Asleep = false;
                _spider.Expression = Face.Surprised;
                _faceTimer = 0.8f;
                _spider.PlayGesture(Gesture.Stretch);
                Say?.Invoke(Lines.Pick(Lines.WakeUp), 3f);
            }
            else
            {
                // The Zs float up from the head (drawn by the spider).
                _faceTimer -= dt;
                if (_faceTimer <= 0) _spider.Expression = Face.Sleeping;
                return;
            }
        }

        // ---- expression timers
        if (_faceTimer > 0)
        {
            _faceTimer -= dt;
            if (_faceTimer <= 0 && _spider.Expression != Face.Sleeping) _spider.Expression = Face.Normal;
        }
        _waveCooldown -= dt;

        if (TickDemo(dt)) return;

        // ---- startle: a fast swipe right next to it
        if (_spider.State == Mode.Grounded && cursorDist < 110 && cursorVel.Length() > 2600)
        {
            React(Face.Surprised, 0.9f);
            HopAwayFrom(cursor);
            _decisionTimer = 1.2f;
            return;
        }

        if (TickSummon(cursor)) return;
        if (TickHunt(dt, cursor)) return;
        if (TickWebTask(dt)) return;
        if (_spider.State == Mode.Hanging) { TickHanging(dt); return; }
        TickLittleScenes(dt, cursorDist);
        // Let gestures finish (peeking is 5 s; cutting it short pops the body back up in one frame).
        if (_spider.State != Mode.Grounded || _spider.IsWalking || _spider.CurrentGesture != Gesture.None) return;

        _decisionTimer -= dt;
        if (_decisionTimer > 0) return;
        _decisionTimer = _rng.RandfRange(1.5f, 5f);
        Decide(cursor, cursorDist);
    }

    void Decide(Vector2 cursor, float cursorDist)
    {
        var surf = _spider.Surface;
        if (surf == null) return;
        _spider.Fidget = false;

        // Upside down or on a wall for a while? Most of the time go back down on silk.
        if (!surf.IsFloorLike && _rng.Randf() < 0.45f)
        {
            if (_spider.DropOnSilk()) return;
            // Too close to the border between two monitors: move away from it first, then drop next time.
            int away = _spider.AwayFromSeam();
            if (away != 0)
            {
                _spider.Walk(_spider.SeamMargin * 1.5f, away);
                _decisionTimer = 0.5f;
                return;
            }
        }

        if (TryStartHunt(cursor, cursorDist)) return;
        if (TryPeek(surf)) return;

        // Now and then: weave a web in a free corner, or go hang from one already there.
        if (AutoWebs && Webs != null)
        {
            float w = _rng.Randf();
            if (w < 0.07f && Webs.Webs.Count(x => x.Alive) < MaxWebs && StartWeb(quiet: true)) return;
            if (w > 0.93f && StartHangOnWeb()) return;
        }

        // Say hi to a cursor that came to visit.
        if (cursorDist < 300 && _waveCooldown <= 0 && _rng.Randf() < 0.5f)
        {
            _waveCooldown = 25;
            _spider.PlayGesture(Gesture.Wave, (cursor - _spider.Contact).Dot(surf.Tangent) >= 0 ? 1 : -1);
            return;
        }

        // Curious about a resting cursor nearby: walk toward it.
        if (cursorDist < 380 && cursorDist > 70 && _rng.Randf() < 0.5f)
        {
            float along = (cursor - _spider.Contact).Dot(surf.Tangent);
            _spider.Walk(Mathf.Min(Mathf.Abs(along) - 30, 260), along >= 0 ? 1 : -1);
            return;
        }

        float roll = _rng.Randf();
        if (roll < 0.3f)
        {
            // Idle: just look around, or one of the little rituals.
            float g = _rng.Randf();
            if (g < 0.35f) _spider.PlayGesture(Gesture.Groom);
            else if (g < 0.5f) _spider.PlayGesture(Gesture.Stretch);
            else if (g < 0.62f) _spider.PlayGesture(Gesture.Tap);
            else if (g < 0.7f) _spider.PlayGesture(Gesture.Dance);
        }
        else if (roll < 0.68f)
        {
            int dir = _rng.Randf() < 0.5f ? -1 : 1;
            if (_rng.Randf() < 0.2f)
            {
                _spider.Walk(_rng.RandfRange(150, 400), dir, 64); // scurry
                // Now and then it trips over its own feet halfway.
                if (_rng.Randf() < 0.15f) _tripTimer = _rng.RandfRange(0.8f, 2f);
            }
            else _spider.Walk(_rng.RandfRange(40, 320), dir, _rng.RandfRange(18, 30));
        }
        else if (roll < 0.9f)
        {
            if (!TryJumpSomewhere()) _spider.Walk(_rng.RandfRange(40, 200), _rng.Randf() < 0.5f ? -1 : 1);
        }
        else
        {
            // Walk to the end of the ledge; OnEdge may then drop on silk.
            _spider.Walk(2000, _rng.Randf() < 0.5f ? -1 : 1);
        }
    }

    void TickHanging(float dt)
    {
        if (_webMeal != null)
        {
            if (_webMeal.Gone || _webMeal.State != FlyState.Stuck) _webMeal = null;
            else if (_spider.HangingWeb != null && _spider.HangingWeb == _webMeal.Web && (_eatTimer -= dt) <= 0)
            {
                Bugs?.Eat(_webMeal);
                _webMeal = null;
                Ate();
            }
        }
        if (_spider.HangingWeb != null)
        {
            // Swing on the web for a while, then lower down to whatever is below.
            _webHangTimer -= dt;
            if (_webHangTimer <= 0 && !_spider.IsDescending) _spider.Descend();
            return;
        }
        if (_spider.IsDescending && _rng.Randf() < dt * 0.25f)
        {
            _spider.HoldSilk();
            _holdTimer = _rng.RandfRange(1f, 3f);
        }
        else if (!_spider.IsDescending && !_spider.IsClimbingSilk)
        {
            _holdTimer -= dt;
            if (_holdTimer <= 0)
            {
                if (_rng.Randf() < 0.3f) _spider.ClimbSilk();
                else _spider.Descend();
            }
        }
    }

    void OnEdge()
    {
        var surf = _spider.Surface;
        if (surf == null) return;
        float r = _rng.Randf();
        if (surf.IsFloorLike && r < 0.4f && _spider.DropOnSilk()) return;
        if (r < 0.7f && TryJumpSomewhere()) return;
        // Turn around.
        _spider.Walk(_rng.RandfRange(60, 220), _spider.S < surf.Length / 2 ? 1 : -1);
    }

    void OnLanded()
    {
        if (_groomOnLanding)
        {
            _groomOnLanding = false;
            _spider.PlayGesture(Gesture.Groom);
        }
        if (_summonSay)
        {
            _summonSay = false;
            React(Face.Happy, 1.5f);
            Say?.Invoke(Lines.Pick(Lines.Summoned), 2.5f);
        }
        if (_rng.Randf() < 0.25f) React(Face.Happy, 1.2f);
        _decisionTimer = Mathf.Min(_decisionTimer, _rng.RandfRange(0.4f, 1.5f));
    }

    bool TryJumpSomewhere()
    {
        var from = _spider.Contact;
        var candidates = _map.All
            .Where(s => s != _spider.Surface && s.Length > 50 && s.IsFloorLike)
            .Select(s => (s, p: s.PointAt(_rng.RandfRange(20, s.Length - 20))))
            .Where(c => c.p.DistanceTo(from) is > 80 and < 650 && c.p.Y > from.Y - 420)
            .ToList();
        if (candidates.Count == 0 && _spider.Surface is { IsFloorLike: true } here)
        {
            // Nowhere else to go: a playful hop along the same ledge.
            float s = Mathf.Clamp(_spider.S + _rng.RandfRange(120, 280) * (_rng.Randf() < 0.5f ? -1 : 1), 40, here.Length - 40);
            _spider.JumpTo(here.PointAt(s));
            return true;
        }
        if (candidates.Count == 0) return false;
        var pick = candidates[_rng.RandiRange(0, candidates.Count - 1)];
        _spider.JumpTo(pick.p);
        return true;
    }

    void HopAwayFrom(Vector2 cursor)
    {
        var surf = _spider.Surface;
        if (surf == null) return;
        float side = (_spider.Contact - cursor).Dot(surf.Tangent) >= 0 ? 1 : -1;
        float s = Mathf.Clamp(_spider.S + side * 90, 0, surf.Length);
        _spider.JumpTo(surf.PointAt(s));
    }

    /// <summary>Little celebratory hop in place (used for reminders and alerts).</summary>
    public void Alert(Face face)
    {
        if (Asleep) { Asleep = false; }
        React(face, 2.5f);
        if (_spider.State != Mode.Grounded || _spider.Surface == null) return;
        if (face == Face.Happy) _spider.PlayGesture(Gesture.Dance);
        else _spider.JumpTo(_spider.Contact);
    }

    public void React(Face face, float seconds)
    {
        _spider.Expression = face;
        _faceTimer = seconds;
    }

    public void WakeUp()
    {
        if (!Asleep) return;
        Asleep = false;
        React(Face.Surprised, 0.8f);
        _spider.PlayGesture(Gesture.Stretch);
    }

    /// <summary>
    /// Reaction to a click: a random pre-programmed line with a matching face and gesture.
    /// Clicking too many times in a row gets an annoyed hop away. Returns the line to show.
    /// </summary>
    public string Poke(Vector2 cursor)
    {
        WakeUp();
        double now = Time.GetTicksMsec() / 1000.0;
        _pokes.Enqueue(now);
        while (_pokes.Count > 0 && now - _pokes.Peek() > 4) _pokes.Dequeue();
        _decisionTimer = 3f;

        if (_pokes.Count >= 5)
        {
            _pokes.Clear();
            React(Face.Sad, 2f);
            HopAwayFrom(cursor);
            return Lines.Pick(Lines.TooManyPokes);
        }

        int level = FriendLevel?.Invoke() ?? 0;
        var pool = Lines.Pokes.AsEnumerable();
        if (level >= 2) pool = pool.Concat(Lines.PokesFriend);
        if (level >= 3) pool = pool.Concat(Lines.PokesBest);
        var reaction = _rng.Randf() < 0.25f ? Lines.Greeting() : Lines.Pick(pool.ToArray());
        React(reaction.Face, 2f);
        if (reaction.Gesture == Gesture.Wave) Greet(cursor);
        else if (reaction.Gesture != Gesture.None) _spider.PlayGesture(reaction.Gesture);
        return reaction.Text;
    }

    // ------------------------------------------------------------------ alerts & the giant web

    /// <summary>Queues an alert: Webi goes to the middle of the screen and shouts it.</summary>
    public void Announce(Notice notice) => _notices.Enqueue(notice);

    /// <summary>Weaves a web over the whole <paramref name="area"/> (a monitor) from its centre.</summary>
    public bool StartGiantWeb(Rect2 area)
    {
        if (_stage != Stage.None || Quiet || Webs == null || Webs.Giant != null) return false;
        EndTask();
        _forGiant = true;
        _giantArea = area;
        BeginTrip(area.GetCenter());
        return true;
    }

    /// <summary>The giant web was shaken apart.</summary>
    public void OnGiantTorn()
    {
        if (_stage is not (Stage.Weaving or Stage.InGiantWeb)) return;
        _stage = Stage.None;
        Asleep = false;
        _spider.Fidget = false;
        React(Face.Surprised, 2);
        Say?.Invoke(Lines.Pick(Lines.GiantTorn), 3);
        if (_spider.State is Mode.Hanging or Mode.Weaving) _spider.Fall();
    }

    void BeginTrip(Vector2 center)
    {
        _center = center;
        _stage = Stage.ToCenter;
        _stageTimer = 15;
        _stageAttempts = 0;
        _spider.StopWalking();
        if (_spider.CurrentGesture != Gesture.None) _spider.StopGesture();
        if (Asleep) { Asleep = false; _spider.Expression = Face.Normal; }
    }

    bool InCenter() =>
        _spider.State == Mode.Hanging && _spider.BodyCenter.DistanceTo(_center) < 160;

    /// <summary>Runs alerts and the giant web. Returns true while they own the spider.</summary>
    bool TickStage(float dt, double idleSeconds)
    {
        if (_stage is Stage.None or Stage.InGiantWeb && _notices.Count > 0 && !Quiet)
        {
            _notice = _notices.Dequeue();
            _forGiant = false;
            EndTask();
            var work = WorkAreaAt?.Invoke(_spider.BodyCenter) ?? new Rect2(_spider.BodyCenter, Vector2.One);
            var center = _stage == Stage.InGiantWeb ? _spider.BodyCenter : work.GetCenter();
            BeginTrip(center);
        }

        switch (_stage)
        {
            case Stage.None:
                return false;

            case Stage.ToCenter:
                if (TickTrip(dt)) Arrive();
                return true;

            case Stage.Shouting:
                _stageTimer -= dt;
                _spider.Expression = _notice.Happy ? Face.Happy : Face.Surprised;
                if (_stageTimer > 0) return true;
                _spider.Expression = Face.Normal;
                if (Webs?.Giant is { } giantWeb && _spider.HangingWeb == giantWeb)
                {
                    _stage = Stage.InGiantWeb;
                    return true;
                }
                _stage = Stage.None;
                if (_notices.Count == 0 && _spider.State == Mode.Hanging) _spider.Descend();
                return true;

            case Stage.Weaving:
            {
                var giant = Webs?.Giant;
                if (giant == null || _spider.State != Mode.Weaving) { _stage = Stage.None; return false; }
                // Long webs go faster so the whole screen takes about half a minute to a minute.
                float speed = Mathf.Max(GiantWeaveSpeed, giant.RouteLength / 60f);
                _weaveDistance += dt * speed;
                _spider.WeavePoint = giant.WalkRoute(_weaveDistance);
                if (_weaveDistance >= giant.RouteLength)
                {
                    // Done: back to the hub to sleep there.
                    _spider.HangFrom(giant);
                    _stage = Stage.InGiantWeb;
                    Asleep = true;
                    _giantHintShown = false;
                    Say?.Invoke(Lines.Pick(Lines.GiantDone), 4);
                }
                return true;
            }

            case Stage.InGiantWeb:
            {
                if (Webs?.Giant == null || _spider.HangingWeb == null) { _stage = Stage.None; Asleep = false; return false; }
                if (Asleep)
                {
                    _spider.Expression = Face.Sleeping;
                    _spider.LookTarget = null;
                    if (idleSeconds < 1)
                    {
                        Asleep = false;
                        React(Face.Surprised, 1);
                        if (!_giantHintShown)
                        {
                            _giantHintShown = true;
                            Say?.Invoke(Lines.Pick(Lines.GiantHint), 5);
                        }
                    }
                }
                else
                {
                    _faceTimer -= dt;
                    if (_faceTimer <= 0) _spider.Expression = Face.Normal;
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>Moves toward the middle of the screen; true once hanging there (or after giving up).</summary>
    bool TickTrip(float dt)
    {
        _stageTimer -= dt;
        if (_stageTimer <= 0) return true; // give up: do it from wherever it is
        switch (_spider.State)
        {
            case Mode.Dragged:
            case Mode.Airborne:
                return false;
            case Mode.Hanging:
                if (InCenter())
                {
                    _spider.HoldSilk();
                    return true;
                }
                // On silk above the middle: keep lowering until level with it.
                if (_spider.HangingWeb == null && Mathf.Abs(_spider.Anchor.X - _center.X) < 220 && _spider.BodyCenter.Y < _center.Y)
                {
                    _spider.Descend(fast: true);
                    return false;
                }
                _spider.Fall();
                return false;
        }

        if (_spider.IsWalking || _spider.IsPreparingJump || _spider.CurrentGesture != Gesture.None) return false;
        var surf = _spider.Surface;
        if (surf != null && surf.IsCeilingLike && Mathf.Abs(_spider.Contact.X - _center.X) < 220)
        {
            _spider.DropOnSilk(fast: true);
            return false;
        }
        if (++_stageAttempts > 3) return true;

        // Super jump to the ceiling right above the middle.
        var ceiling = _map.All
            .Where(x => x.IsCeilingLike && x.Owner < 0 && Mathf.Min(x.A.X, x.B.X) < _center.X && Mathf.Max(x.A.X, x.B.X) > _center.X && x.A.Y < _center.Y)
            .OrderByDescending(x => x.A.Y)
            .FirstOrDefault();
        if (ceiling == null) return true;
        _spider.JumpTo(new Vector2(_center.X, ceiling.A.Y));
        return false;
    }

    void Arrive()
    {
        if (_forGiant && Webs != null)
        {
            var giant = Webs.BeginGiant(_giantArea);
            giant.PlanRoute(_spider.Spinnerets);
            _weaveDistance = 0;
            _spider.BeginWeaving(_spider.Spinnerets);
            _stage = Stage.Weaving;
            Say?.Invoke(Lines.Pick(Lines.GiantStart), 3);
            return;
        }
        _stage = Stage.Shouting;
        _stageTimer = 7;
        Shout?.Invoke(_notice);
    }

    // ------------------------------------------------------------------ webs

    /// <summary>Goes to the nearest free corner and weaves a web there. Returns false when there is none.</summary>
    public bool StartWeb(bool quiet = false)
    {
        if (Webs == null || _spider.State != Mode.Grounded) return false;
        var spots = Webs.Spots(_map);
        if (spots.Count == 0)
        {
            if (!quiet) Say?.Invoke(Lines.Pick(Lines.NoWebSpot), 4);
            return false;
        }
        // Prefer corners reachable by walking, then the nearest.
        var pick = spots.OrderBy(sp => Reachable(sp.Stand) ? 0 : 1)
            .ThenBy(sp => sp.StandPoint.DistanceTo(_spider.Contact)).First();
        BeginTask(WebTask.GoBuild, pick.Key);
        if (!quiet) Say?.Invoke(Lines.Pick(Lines.WebStart), 3);
        return true;
    }

    bool StartHangOnWeb()
    {
        if (Webs == null || _spider.State != Mode.Grounded) return false;
        var done = Webs.Webs.Where(w => w.Alive && w.Visible && w.Progress >= 1).Select(w => w.Key).ToHashSet();
        var spots = Webs.Spots(_map, freeOnly: false).Where(sp => done.Contains(sp.Key)).ToList();
        if (spots.Count == 0) return false;
        BeginTask(WebTask.GoHang, spots[_rng.RandiRange(0, spots.Count - 1)].Key);
        return true;
    }

    void BeginTask(WebTask task, string key)
    {
        _task = task;
        _taskKey = key;
        _taskAttempts = 0;
        _taskTimer = 45;
        _spider.StopWalking();
    }

    bool Reachable(Surface target)
    {
        var start = _spider.Surface;
        if (start == null) return false;
        if (start == target) return true;
        foreach (var forward in new[] { true, false })
        {
            var cur = forward ? start.Next : start.Prev;
            for (int i = 0; cur != null && cur != start && i < 40; i++, cur = forward ? cur.Next : cur.Prev)
                if (cur == target) return true;
        }
        return false;
    }

    /// <summary>Runs the current web task. Returns true while it owns the spider.</summary>
    bool TickWebTask(float dt)
    {
        if (_task == WebTask.None || Webs == null) return false;
        int px = _spider.PixelScale;

        if (_task == WebTask.Building)
        {
            var web = Webs.Find(_taskKey);
            if (web == null || _spider.State != Mode.Weaving)
            {
                // Interrupted (picked up, window closed...): the half-made web is abandoned.
                if (web != null) web.Alive = false;
                if (_spider.State == Mode.Weaving) _spider.Fall();
                EndTask();
                return false;
            }
            // Crawl along the web laying each thread in turn; the silk ends right at the spinnerets.
            _weaveDistance += dt * CornerWeaveSpeed;
            _spider.WeavePoint = web.WalkRoute(_weaveDistance);
            if (_weaveDistance >= web.RouteLength)
            {
                EndTask();
                Webs.Trim(MaxWebs);
                React(Face.Happy, 2);
                Say?.Invoke(Lines.Pick(Lines.WebDone), 4);
                HangOn(web);
            }
            return true;
        }

        _taskTimer -= dt;
        if (_taskTimer <= 0) { EndTask(); return false; }
        // Still on the way (walking, mid-jump, finishing a gesture).
        if (_spider.State != Mode.Grounded || _spider.IsWalking || _spider.CurrentGesture != Gesture.None) return true;

        var spot = Webs.Spots(_map, freeOnly: _task == WebTask.GoBuild).FirstOrDefault(sp => sp.Key == _taskKey);
        if (spot == null) { EndTask(); return false; }

        if (_spider.Contact.DistanceTo(spot.StandPoint) < 26 * px)
        {
            if (_task == WebTask.GoBuild)
            {
                var web = Webs.Begin(spot);
                web.PlanRoute(_spider.Spinnerets);
                _weaveDistance = 0;
                _spider.BeginWeaving(_spider.Spinnerets);
                _task = WebTask.Building;
            }
            else
            {
                var web = Webs.Find(_taskKey);
                EndTask();
                if (web != null) HangOn(web);
            }
            return true;
        }

        if (++_taskAttempts > 5) { NoWayThere(); return false; }
        if (!_spider.WalkTo(spot.Stand, spot.StandS, speed: 60))
        {
            // Another window or monitor island: jump over if it's within reach, otherwise walk closer first.
            if (_spider.Contact.DistanceTo(spot.StandPoint) < 700) _spider.JumpTo(spot.StandPoint);
            else if (!WalkToward(spot.StandPoint)) { NoWayThere(); return false; }
        }
        return true;
    }

    /// <summary>Walks along the current surface toward a point, stopping at its end (never round a corner).</summary>
    bool WalkToward(Vector2 target)
    {
        var surf = _spider.Surface;
        if (surf == null) return false;
        int px = _spider.PixelScale;
        float along = (target - _spider.Contact).Dot(surf.Tangent);
        int dir = along >= 0 ? 1 : -1;
        float room = dir > 0 ? surf.Length - _spider.S : _spider.S;
        float dist = Mathf.Min(Mathf.Min(Mathf.Abs(along), room), 200 * px);
        if (dist < 40 * px) return false;
        _spider.Walk(dist, dir, 60);
        return true;
    }

    /// <summary>The web task found no way to its corner; a trip for a stuck fly counts toward giving it up.</summary>
    void NoWayThere()
    {
        if (_task == WebTask.GoHang && _webMeal != null && _mealFails.ContainsKey(_webMeal)) _mealFails[_webMeal]++;
        EndTask();
    }

    void HangOn(Web web)
    {
        _spider.HangFrom(web);
        _webHangTimer = _rng.RandfRange(8, 20);
        if (_webMeal?.Web == web)
        {
            _eatTimer = 1.5f;
            _webHangTimer = Mathf.Max(_webHangTimer, 4);
            return;
        }
        if (_rng.Randf() < 0.5f) Say?.Invoke(Lines.Pick(Lines.WebHang), 3);
    }

    void EndTask()
    {
        _task = WebTask.None;
    }

    /// <summary>Busy with an alert or the giant web (no small talk then).</summary>
    public bool Busy => _stage != Stage.None;

    /// <summary>A comment about what the user is doing: face + gesture, returns the line to show.</summary>
    public string Remark(Reaction reaction)
    {
        React(reaction.Face, 2.5f);
        if (_spider.State == Mode.Grounded && !_spider.IsWalking && reaction.Gesture != Gesture.None && _task == WebTask.None)
            _spider.PlayGesture(reaction.Gesture);
        return reaction.Text;
    }

    /// <summary>Waves at the cursor.</summary>
    public void Greet(Vector2 cursor)
    {
        if (_spider.Surface == null) return;
        _waveCooldown = 25;
        _spider.PlayGesture(Gesture.Wave, (cursor - _spider.Contact).Dot(_spider.Surface.Tangent) >= 0 ? 1 : -1);
    }

    // ------------------------------------------------------------------ hunting

    /// <summary>A fly got stuck in a web: go and get it soon.</summary>
    public void OnFlyStuck() => _decisionTimer = Mathf.Min(_decisionTimer, 1f);

    bool TryStartHunt(Vector2 cursor, float cursorDist)
    {
        // Decisions only come while idle on the ground, so a trip to a web for a meal is over (eaten or failed):
        // forget it, so a failed trip can be tried again.
        _webMeal = null;
        var surf = _spider.Surface;
        if (surf == null || !surf.IsFloorLike) return false;

        // A fly stuck in one of the corner webs: go hang there and eat it.
        if (Bugs != null && Webs != null)
        {
            foreach (var gone in _mealFails.Keys.Where(f => f.Gone || f.State != FlyState.Stuck).ToList())
                _mealFails.Remove(gone);
            var stuck = Bugs.Flies.FirstOrDefault(f => f.State == FlyState.Stuck && f.Web is { Alive: true, Visible: true, Giant: false }
                                                       && _mealFails.GetValueOrDefault(f) < MaxMealFails);
            if (stuck != null && Webs.Spots(_map, freeOnly: false).Any(sp => sp.Key == stuck.Web!.Key))
            {
                // A trip that didn't make it is tried again quietly; after a few with no way there the fly is left alone.
                if (_mealFails.TryAdd(stuck, 0)) Say?.Invoke(Lines.Pick(Lines.FlyInWeb), 3);
                _webMeal = stuck;
                BeginTask(WebTask.GoHang, stuck.Web!.Key);
                return true;
            }
        }

        if (Bugs != null && _huntCooldown <= 0)
        {
            var fly = Bugs.Flies
                .Where(f => f.State is FlyState.Flying or FlyState.Resting && f.Pos.DistanceTo(_spider.BodyCenter) < 700)
                .OrderBy(f => f.Pos.DistanceTo(_spider.BodyCenter)).FirstOrDefault();
            if (fly != null && _rng.Randf() < 0.75f)
            {
                _prey = fly;
                _preyCursor = false;
                _hunt = Hunt.Approach;
                _huntTimer = 20;
                _huntSteps = 0;
                if (_rng.Randf() < 0.5f) Say?.Invoke(Lines.Pick(Lines.FlySpotted), 2.5f);
                return true;
            }
        }

        // The cursor resting nearby looks like prey.
        if (_cursorHuntCooldown <= 0 && _cursorStill > 1.5f && cursorDist is > 120 and < 420 && _rng.Randf() < 0.3f)
        {
            _cursorHuntCooldown = 120;
            _preyCursor = true;
            _prey = null;
            _stalkCursor = cursor;
            _hunt = Hunt.Stalk;
            _huntTimer = 6;
            _spider.PlayGesture(Gesture.Stalk, (cursor - _spider.Contact).Dot(surf.Tangent) >= 0 ? 1 : -1);
            return true;
        }
        return false;
    }

    void EndHunt()
    {
        if (_hunt == Hunt.None) return;
        _hunt = Hunt.None;
        _prey = null;
        _preyCursor = false;
        _huntCooldown = _rng.RandfRange(15, 40);
    }

    /// <summary>Runs a hunt. Returns true while it owns the spider.</summary>
    bool TickHunt(float dt, Vector2 cursor)
    {
        if (_hunt == Hunt.None) return false;
        if (!_preyCursor && (_prey == null || _prey.Gone || (_hunt != Hunt.Pounce && _prey.State is FlyState.Stuck or FlyState.Leaving)))
        {
            EndHunt();
            return false;
        }
        var target = _preyCursor ? cursor : _prey!.Pos;
        _spider.LookTarget = target;
        _huntTimer -= dt;
        if (_huntTimer <= 0) { EndHunt(); return false; }
        int px = _spider.PixelScale;

        switch (_hunt)
        {
            case Hunt.Approach:
            {
                if (_spider.State != Mode.Grounded) { EndHunt(); return false; }
                if (_spider.IsWalking || _spider.IsPreparingJump || _spider.CurrentGesture != Gesture.None) return true;
                var surf = _spider.Surface;
                if (surf == null) { EndHunt(); return false; }
                if (target.DistanceTo(_spider.BodyCenter) < 480)
                {
                    _hunt = Hunt.Stalk;
                    // A resting fly sometimes notices the spider before the pounce.
                    _preyNotices = _prey!.State == FlyState.Resting ? _rng.Randf() < 0.25f : _rng.Randf() < 0.4f;
                    _spider.PlayGesture(Gesture.Stalk, (target - _spider.Contact).Dot(surf.Tangent) >= 0 ? 1 : -1);
                    return true;
                }
                float along = (target - _spider.Contact).Dot(surf.Tangent);
                if (++_huntSteps > 4 || Mathf.Abs(along) < 80) { EndHunt(); return false; }
                _spider.Walk(Mathf.Min(Mathf.Abs(along) - 160, 320), along >= 0 ? 1 : -1, 40);
                return true;
            }

            case Hunt.Stalk:
                if (_spider.State != Mode.Grounded) { EndHunt(); return false; }
                if (_spider.CurrentGesture == Gesture.Stalk)
                {
                    if (_preyNotices && _prey != null && _rng.Randf() < dt * 1.2f)
                    {
                        _preyNotices = false;
                        Bugs?.Startle(_prey, _spider.BodyCenter);
                    }
                    return true;
                }
                if (_preyCursor && cursor.DistanceTo(_stalkCursor) > 25)
                {
                    // The cursor got away before the pounce.
                    React(Face.Normal, 1);
                    _spider.PlayGesture(Gesture.Groom);
                    Say?.Invoke(Lines.Pick(Lines.CursorEscaped), 2.5f);
                    EndHunt();
                    return true;
                }
                var aim = target;
                if (!_preyCursor && _prey!.State == FlyState.Flying)
                {
                    // Lead a flying target a little.
                    float t = Mathf.Clamp(_spider.Contact.DistanceTo(aim) / 750f, 0.32f, 0.85f) + 0.15f;
                    aim += _prey.Vel * t * 0.6f;
                }
                // JumpTo aims the feet: put the body (half a body above them) through the target.
                _spider.JumpTo(aim + new Vector2(0, Spider.BodyHeight * px * 0.6f));
                _hunt = Hunt.Pounce;
                _caught = false;
                _huntTimer = 3;
                return true;

            case Hunt.Pounce:
                if (_spider.IsPreparingJump || _spider.State == Mode.Airborne)
                {
                    if (!_caught && target.DistanceTo(_spider.BodyCenter) < 28 * px)
                    {
                        _caught = true;
                        if (_preyCursor)
                        {
                            React(Face.Happy, 2);
                            Say?.Invoke(Lines.Pick(Lines.CursorCaught), 2.5f);
                        }
                        else
                        {
                            Bugs?.Eat(_prey!);
                            Ate();
                        }
                    }
                    return true;
                }
                if (!_caught)
                {
                    React(Face.Sad, 1.5f);
                    Say?.Invoke(Lines.Pick(_preyCursor ? Lines.CursorEscaped : Lines.FlyMissed), 2.5f);
                    if (_prey != null) Bugs?.Startle(_prey, _spider.BodyCenter);
                }
                EndHunt();
                return false;
        }
        return false;
    }

    /// <summary>A fly was eaten (pounced on or taken from a web).</summary>
    void Ate()
    {
        React(Face.Happy, 2);
        _groomOnLanding = _spider.State == Mode.Airborne;
        Say?.Invoke(Lines.Pick(Lines.FlyCaught), 2.5f);
        FlyEaten?.Invoke();
    }

    // ------------------------------------------------------------------ little scenes

    /// <summary>Hides behind the window edge it stands on, only the eyes peeking out.</summary>
    bool TryPeek(Surface surf)
    {
        if (_peekCooldown > 0 || surf.Owner < 0 || !surf.IsFloorLike || _rng.Randf() > 0.07f) return false;
        // Not right next to the top or bottom of the screen, where the body can't sink.
        var work = WorkAreaAt?.Invoke(_spider.Contact);
        if (work is { } w && (_spider.Contact.Y < w.Position.Y + 120 || _spider.Contact.Y > w.End.Y - 60)) return false;
        _peekCooldown = _rng.RandfRange(90, 200);
        _peeking = true;
        _spider.PlayGesture(Gesture.Peek);
        return true;
    }

    /// <summary>Peekaboo, the stumble and its aftermath.</summary>
    void TickLittleScenes(float dt, float cursorDist)
    {
        if (_peeking)
        {
            if (_spider.CurrentGesture == Gesture.Peek && cursorDist < 170)
            {
                // Found! Pops up with a little hop.
                _peeking = false;
                _spider.StopGesture();
                React(Face.Happy, 1.5f);
                Say?.Invoke(Lines.Pick(Lines.Peekaboo), 2);
                _spider.JumpTo(_spider.Contact);
            }
            else if (_spider.CurrentGesture != Gesture.Peek)
            {
                _peeking = false;
                if (_rng.Randf() < 0.4f) Say?.Invoke(Lines.Pick(Lines.Peekaboo), 2);
            }
        }

        if (_tripTimer > 0)
        {
            if (_spider.State != Mode.Grounded || !_spider.IsWalking) _tripTimer = 0;
            else if ((_tripTimer -= dt) <= 0)
            {
                _spider.PlayGesture(Gesture.Trip, _spider.WalkDir);
                _tripLine = true;
            }
        }
        if (_tripLine && _spider.CurrentGesture != Gesture.Trip)
        {
            _tripLine = false;
            Say?.Invoke(Lines.Pick(Lines.Tripped), 2.5f);
            _spider.PlayGesture(Gesture.Groom);
        }
    }

    /// <summary>Recent readings of the window under it moving (time, px/s).</summary>
    readonly Queue<(double t, float speed)> _carry = new();

    /// <summary>
    /// The window under it moved (speed in px/s): a ride, or thrown off when shaken hard. Only sustained movement
    /// over several readings counts: maximizing or snapping a window jumps it once and must not throw her off.
    /// </summary>
    public void OnCarried(float speed)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        _carry.Enqueue((now, speed));
        while (_carry.Count > 0 && now - _carry.Peek().t > 0.8) _carry.Dequeue();
        if (_stage != Stage.None || _spider.State != Mode.Grounded || Asleep) return;
        int fast = _carry.Count(c => c.speed > 1500), moving = _carry.Count(c => c.speed > 200);
        if (fast >= 3)
        {
            _carry.Clear();
            EndHunt();
            EndTask();
            _spider.Fling(new Vector2(_rng.RandfRange(-350, 350), -650));
            React(Face.Surprised, 1.5f);
            Say?.Invoke(Lines.Pick(Lines.ShakenOff), 2.5f);
            _carriedCooldown = 10;
            return;
        }
        if (moving >= 2 && _carriedCooldown <= 0)
        {
            _carriedCooldown = 45;
            React(Face.Surprised, 1.2f);
            Say?.Invoke(Lines.Pick(Lines.Carried), 2.5f);
        }
    }

    /// <summary>Called with the shortcut: comes to the floor under the cursor. False while busy with an alert.</summary>
    public bool Summon(Vector2 cursor)
    {
        WakeUp();
        if (_stage != Stage.None) return false;
        EndTask();
        EndHunt();
        _summonAt = cursor;
        _summonPending = true;
        if (_spider.State is Mode.Hanging or Mode.Weaving) _spider.Fall();
        return true;
    }

    bool TickSummon(Vector2 cursor)
    {
        if (!_summonPending) return false;
        if (_spider.State is Mode.Airborne or Mode.Dragged || _spider.IsPreparingJump) return true;
        if (_spider.State != Mode.Grounded) return true;
        _summonPending = false;
        var floor = _map.FloorBelow(_summonAt, out _, out float s);
        var target = floor != null ? floor.PointAt(Mathf.Clamp(s, 40, Mathf.Max(40, floor.Length - 40))) : _summonAt;
        if (_spider.Contact.DistanceTo(target) < 60)
        {
            Say?.Invoke(Lines.Pick(Lines.Summoned), 2.5f);
            Greet(cursor);
            return true;
        }
        _summonSay = true;
        _spider.JumpTo(target);
        return true;
    }
}

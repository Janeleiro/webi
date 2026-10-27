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
    float _zzzTimer;
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

        // The eyes always follow the mouse (except while asleep, below).
        _spider.LookTarget = cursor;
        if (TickStage(dt, idleSeconds)) return;

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
                _faceTimer -= dt;
                if (_faceTimer <= 0) _spider.Expression = Face.Sleeping;
                _zzzTimer -= dt;
                if (_zzzTimer <= 0) { _zzzTimer = 6f; Say?.Invoke("z z z", 2.5f); }
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

        if (TickWebTask(dt)) return;
        if (_spider.State == Mode.Hanging) { TickHanging(dt); return; }
        if (_spider.State != Mode.Grounded || _spider.IsWalking) return;

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
        if (!surf.IsFloorLike && _rng.Randf() < 0.45f && _spider.DropOnSilk()) return;

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
            if (_rng.Randf() < 0.2f) _spider.Walk(_rng.RandfRange(150, 400), dir, 64); // scurry
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

        var reaction = _rng.Randf() < 0.25f ? Lines.Greeting() : Lines.Pick(Lines.Pokes);
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
                    _zzzTimer -= dt;
                    if (_zzzTimer <= 0) { _zzzTimer = 6f; Say?.Invoke("z z z", 2.5f); }
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

        if (++_taskAttempts > 5) { EndTask(); return false; }
        if (!_spider.WalkTo(spot.Stand, spot.StandS, speed: 60))
        {
            // Another window or monitor island: jump over if it's within reach.
            if (_spider.Contact.DistanceTo(spot.StandPoint) < 700) _spider.JumpTo(spot.StandPoint);
            else { EndTask(); return false; }
        }
        return true;
    }

    void HangOn(Web web)
    {
        _spider.HangFrom(web);
        _webHangTimer = _rng.RandfRange(8, 20);
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
}

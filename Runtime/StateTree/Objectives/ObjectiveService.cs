using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>
    /// THE QUEST LINE'S KEEPER (M24) — one current objective, watched four ways. The
    /// toolset side owns the SEMANTICS: what each kind means, when it completes, what the
    /// chain does next, and where the arrow should point (the nearest citizen carrying the
    /// row's tag — nearest wins, always). The GAME owns the FACTS and reports them through
    /// the small surface here — <see cref="ReportKill"/>, <see cref="ReportPickupCount"/>,
    /// <see cref="ReportDialogFinished"/> — so this service references no game system, and
    /// a different game wires a different bridge. MoveTo needs no report at all: the world
    /// registry and the player's position are toolset facts, checked here.
    /// </summary>
    [ServiceActionContract(FocusAction, "value = the target's stableId, or empty for the "
        + "nearest one")]
    [ServiceActionContract(CompleteAction, "value = a row's name - completes it when it is "
        + "current, or advances the released zone whose cursor it is; empty completes "
        + "whatever is current")]
    [ServiceAnnouncement(CompletedAnnouncement, typeof(string),
        "payload = the completed row's name; the serial beside it fires once per completion")]
    public sealed class ObjectiveService : StateTreeService
    {

        /// <summary>Built by its scope's installer (M33): the quest line, with the zones and
        /// objectives its def declares.</summary>
        public ObjectiveService(StateTreeContextHost scope, ServiceDef definition)
            : base(scope, definition)
        {
        }

        /// <summary>"SHOW ME." The line's one verb that changes nothing about the line —
        /// asked by the objective banner, by a tap on a pointer, or by any tree that wants
        /// to draw the eye to what it is asking for.</summary>
        public const string FocusAction = "objective-focus";

        /// <summary>"THE FLOW SAYS WHEN." Completes the CURRENT objective — the verb for a
        /// step no watcher can see (a door a tree opened). Named, the value is a guard
        /// against a stale write; empty completes whatever is current.</summary>
        public const string CompleteAction = "objective-complete";

        /// <summary>Announced on the scope board each time a row completes - payload is the
        /// row's NAME; a film or a flow keys off the serial beside it.</summary>
        public const string CompletedAnnouncement = "objective.completed";


        [Tooltip("Activated once when the service starts, empty = nothing runs until an "
            + "ActivateObjectiveTask (or code) asks. The chain takes it from there.")]
        public StateTreeEntryRef<ObjectiveDef> startingObjective = new StateTreeEntryRef<ObjectiveDef>();

        /// <summary>The objective being pursued, or null between chains.</summary>
        public ObjectiveDef current { get; private set; }

        /// <summary>Kills landed / items carried toward <see cref="ObjectiveDef.count"/>.</summary>
        public int progress { get; private set; }



        /// <summary>
        /// THE SENTENCE, PUBLISHED (M34): what the line is asking for right now, as a thing a
        /// skin binds to instead of recomputing every frame. Refreshed by this service when its
        /// own state moves — the view is told, never asked.
        /// </summary>
        public ObjectiveBanner banner { get; } = new ObjectiveBanner();


        /// <summary>Rebuild the published sentence; raise only when it actually moved, because
        /// a change event per frame is a poll wearing a different hat.</summary>
        private void RefreshBanner()
        {
            ObjectiveDef row = current;
            string title = "";
            string zone = "";
            Color accent = Color.white;

            if (row != null)
            {
                title = string.IsNullOrEmpty(row.displayName) ? row.name : row.displayName;
                bool counted = row.kind == ObjectiveKind.EnemyKill
                    || row.kind == ObjectiveKind.Pickup;
                if (counted && row.count > 1)
                    title += "  " + progress + " / " + row.count;
                accent = row.accentColor;

                ZoneDef zoneRow = activeZoneRow;
                zone = zoneRow != null && zoneRow.asset != null
                    ? (string.IsNullOrEmpty(zoneRow.asset.displayName)
                        ? zoneRow.asset.name : zoneRow.asset.displayName)
                    : "";
            }

            if (banner.title == title && banner.zone == zone && banner.accent == accent
                && banner.asking == (row != null))
                return;

            banner.title = title;
            banner.zone = zone;
            banner.accent = accent;
            banner.asking = row != null;
            m_Widget?.AccentChanged();
        }

        /// <summary>The quest line's saveable heart: every zone's cursor, the linear
        /// line's, and what was current — row NAMES, the registry keys. A serializable
        /// plain class so any save system can carry it without knowing objectives.</summary>
        [Serializable]
        public sealed class SaveState
        {
            public bool hasState;
            public List<string> zoneNames = new List<string>();
            public List<string> zoneCursors = new List<string>();   // "" = that stack is done
            public string linearCursor = "";
            public string currentName = "";
            public int progress;
        }

        private bool m_AutoStarted;
        private readonly List<WorldObjectBehaviour> m_Buffer = new List<WorldObjectBehaviour>();

        /// <summary>Each zone's place in its ORDERED stack (by the zone row's id) —
        /// resumed when the zone is nearest again, done when it runs past the end.
        /// Progress is PER ZONE: switching away and back resumes (the HT model).</summary>
        private readonly Dictionary<string, int> m_ZoneIndex =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private bool m_ZonesIndexed;
        private string m_ActiveZone = "";

        /// <summary>The zoneless line's own place — held while a zone has the screen, so
        /// falling back resumes it instead of losing it.</summary>
        private ObjectiveDef m_LinearCursor;

        /// <summary>The id of the zone whose stack is asked for, empty for the linear line.</summary>
        public string activeZone => m_ActiveZone;

        /// <summary>The asking zone's row — the widget's title line. Null on the linear line.</summary>
        public ZoneDef activeZoneRow => FindZoneById(m_ActiveZone);

        /// <summary>The zone a TREE is asking for, empty while distance decides — see
        /// <see cref="AskZone"/>.</summary>
        public string askedZone => m_AskedZone;

        private string m_AskedZone = "";

        /// <summary>The zone row by NAME, from the same catalog the orchestrator reads —
        /// what <see cref="RunZoneTask"/> resolves its picked row against.</summary>
        public ZoneDef FindZone(string zoneName)
        {
            ZoneRegistry zones = Zones();
            return zones != null && !string.IsNullOrEmpty(zoneName)
                ? zones.FindByName(zoneName) as ZoneDef
                : null;
        }

        /// <summary>
        /// A TREE ASKS A ZONE: the session tree, not distance, says whose stack asks — the
        /// orchestrator stands down while the ask stands, and no volume is needed, so a zone
        /// with no placed geometry still runs. The cursor is the zone's own and survives the
        /// ask: a pre-empted state re-asking resumes where the stack stood.
        /// </summary>
        public void AskZone(ZoneDef zone)
        {
            if (zone == null || string.IsNullOrEmpty(zone.id))
                return;
            IndexZones();
            if (!m_ZoneIndex.ContainsKey(zone.id))
                m_ZoneIndex.Add(zone.id, 0);
            m_AskedZone = zone.id;
            m_ActiveZone = zone.id;
            ObjectiveDef cursor = StackRowAt(zone, m_ZoneIndex[zone.id]);
            if (!ReferenceEquals(cursor, current))
            {
                current = cursor;
                progress = 0;
            }
            Moved();
            SettleGates();
        }

        /// <summary>The ask withdrawn — only by whoever holds it. The linear line resumes
        /// exactly where it stood; distance competes again next tick.</summary>
        public void ReleaseZone(ZoneDef zone)
        {
            if (zone == null
                || !string.Equals(m_AskedZone, zone.id, StringComparison.Ordinal))
                return;
            m_AskedZone = "";
            m_ActiveZone = "";
            if (!ReferenceEquals(current, m_LinearCursor))
            {
                current = m_LinearCursor;
                progress = 0;
            }
            Moved();
        }

        /// <summary>Ran past its end — what a state waiting on a zone completes on.</summary>
        public bool ZoneDone(ZoneDef zone)
        {
            if (zone == null || zone.asset == null || zone.asset.entries.Count == 0)
                return true;
            IndexZones();
            return m_ZoneIndex.TryGetValue(zone.id, out int index)
                && index >= zone.asset.entries.Count;
        }

        /// <summary>The zone catalog: the first ZoneRegistry the objective registry lists
        /// in dependsOn — the same provenance chain every picked reference uses.</summary>
        private ZoneRegistry Zones()
        {
            ObjectiveRegistry registry = catalog;
            var depends = registry != null ? registry.dependsOn : null;
            for (int i = 0; depends != null && i < depends.Count; i++)
            {
                if (depends[i] is ZoneRegistry zones)
                    return zones;
            }
            return null;
        }

        private ZoneDef FindZoneById(string zoneId)
        {
            ZoneRegistry zones = Zones();
            if (zones == null || string.IsNullOrEmpty(zoneId))
                return null;
            for (int i = 0; i < zones.entries.Count; i++)
            {
                if (zones.entries[i] != null
                    && string.Equals(zones.entries[i].id, zoneId, StringComparison.Ordinal))
                    return zones.entries[i];
            }
            return null;
        }

        private static ObjectiveDef StackRowAt(ZoneDef zone, int index)
        {
            if (zone == null || zone.asset == null
                || index < 0 || index >= zone.asset.entries.Count)
                return null;
            return zone.asset.entries[index];
        }

        public ObjectiveRegistry catalog =>
            definition != null ? definition.registry as ObjectiveRegistry : null;

        public ObjectiveDef Find(string objectiveName)
        {
            ObjectiveRegistry registry = catalog;
            return registry != null && !string.IsNullOrEmpty(objectiveName)
                ? registry.FindByName(objectiveName) as ObjectiveDef
                : null;
        }

        public void Activate(ObjectiveDef objective)
        {
            current = objective;
            progress = 0;
            if (objective != null)
                m_LinearCursor = objective;
            Moved();
        }

        /// <summary>
        /// A PICKUP OBJECTIVE ASKS THE BAG (M39.2b) the moment it becomes current — what is
        /// already carried counts from the start. The bag reports every later change itself
        /// (<see cref="ReportPickupCount"/>); this is the one direction the bag cannot know
        /// about, so the quest line pulls. The bag is root-scoped and this is not, so it is
        /// asked for on first need rather than assumed at construction.
        /// </summary>
        private void AskTheBag()
        {
            if (current == null || current.kind != ObjectiveKind.Pickup)
                return;
            m_Bag ??= StateTreeContextHost.FindService<IBag>(scope.gameObject);
            if (m_Bag != null)
                ReportPickupCount(current.target.entryName, m_Bag.Count(current.target.entryName));
        }

        private IBag m_Bag;

        /// <summary>
        /// THE QUEST LINE MOVED — every effect of a change of <see cref="current"/>, in one
        /// place and in order (meta-rule 1): knock on the save, redraw the banner, ask the bag
        /// what is already carried, and put the marker where the objective is.
        /// </summary>
        private void Moved()
        {
            Knock();
            RefreshBanner();
            AskTheBag();
            RefreshMarker();
        }

        // ---- the marker: the objective standing in the world (M42.3) -------------------

        [ServiceSetting(true, "Put a marker in the world on the objective's target — for the "
            + "rows that ask for one (worldMarker). Off for a level that would rather stay clean.")]
        public bool worldMarkers = true;

        /// <summary>How far above the target's feet the marker sits.</summary>
        private const float k_MarkerGroundOffset = 0.02f;

        private GameObject m_Marker;
        private ObjectiveDef m_Marked;

        /// <summary>
        /// THE MARKER IS THE OBJECTIVE'S (M42.3): built from this def's body the moment an
        /// objective that asks for one becomes current, and destroyed the moment that
        /// objective is done or replaced — it is part of the objective, not a thing the level
        /// keeps around. Its colour is the row's accent, worn through <see cref="IWorldTintable"/>
        /// so the marker, the arrow and the objective line are visibly one thing.
        /// </summary>
        private void RefreshMarker()
        {
            bool wanted = current != null && current.worldMarker && worldMarkers
                && definition != null && definition.body.IsThing;
            if (!wanted)
            {
                DestroyMarker();
                return;
            }
            if (m_Marker != null && ReferenceEquals(m_Marked, current))
                return;
            DestroyMarker();

            Vector3? at = CurrentTargetPosition();
            var row = new LevelObjectDef
            {
                id = "objective.marker", name = "Objective marker · " + current.name
            };
            m_Marker = ServiceBodyFactory.Build(definition, row, scope.transform,
                MarkerPosition(at ?? scope.transform.position), Quaternion.identity);
            m_Marked = current;
            if (m_Marker == null)
                return;
            m_Marker.GetComponentInChildren<IWorldTintable>(true)?.SetTint(current.accentColor);
            m_Marker.SetActive(at != null);
        }

        /// <summary>Every frame the marker stands: on the nearest carrier of the row's tag,
        /// or nowhere — an objective whose place is in another level, or a kill count with
        /// nobody left, simply has no place, which is a normal answer.</summary>
        private void PlaceMarker()
        {
            if (m_Marker == null)
                return;
            Vector3? at = CurrentTargetPosition();
            if (at == null)
            {
                if (m_Marker.activeSelf)
                    m_Marker.SetActive(false);
                return;
            }
            if (!m_Marker.activeSelf)
                m_Marker.SetActive(true);
            m_Marker.transform.position = MarkerPosition(at.Value);
        }

        private static Vector3 MarkerPosition(Vector3 target)
        {
            return new Vector3(target.x, target.y + k_MarkerGroundOffset, target.z);
        }

        private void DestroyMarker()
        {
            if (m_Marker != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(m_Marker);
                else
                    UnityEngine.Object.DestroyImmediate(m_Marker);
            }
            m_Marker = null;
            m_Marked = null;
        }

        /// <summary>The marker this quest line is holding up, or null — the test's window.</summary>
        public GameObject marker => m_Marker;

        /// <summary>The quest line moved — say so to whoever listens (until M40.4 deletes the
        /// event) and knock on the save (meta-rule 1: a call from the change, not a subscription
        /// on the save). The save is optional and asked for at the first knock.</summary>
        private void Knock()
        {
            m_Autosave ??= StateTreeContextHost.FindService<IAutosave>(scope.gameObject);
            m_Autosave?.MarkDirty();
        }

        private IAutosave m_Autosave;
        private ILookAt m_Look;

        /// <summary>
        /// THE WIDGET THIS QUEST LINE TELLS (M40.4, meta-rule 1). The session shows the HUD's
        /// objective piece before any level exists, so the quest line — born with its level —
        /// asks the UI service for it once, at its start, and holds it: bound now, told on
        /// every completion and every change of accent, released when this level goes. The
        /// widget neither finds the quest line nor watches for it to be replaced.
        /// </summary>
        private ObjectiveWidgetView m_Widget;

        protected override void OnStarted()
        {
            m_Widget = Ui != null ? Ui.Shown<ObjectiveWidgetView>() : null;
            m_Widget?.Bind(this);
        }

        public override void Dispose()
        {
            m_Widget?.Unbind(this);
            m_Widget = null;
            DestroyMarker();   // the level goes, and the objective's marker with it
            base.Dispose();
        }

        /// <summary>Complete the CURRENT objective and let the chain speak: its
        /// nextOnComplete advances THIS stack (the zone's cursor when the row belongs to
        /// one), and the orchestrator re-picks — a finished zone stops competing.</summary>
        public void Complete()
        {
            ObjectiveDef done = current;
            if (done == null)
                return;
            current = null;
            progress = 0;
            m_Widget?.Completed(done);
            Announce(CompletedAnnouncement, done.name);
            if (!string.IsNullOrEmpty(done.completeRequestKey))
            {
                StateTreeContextHost top = StateTreeContextHost.Resolve(
                    scope.gameObject, StateTreeContextKind.Root);
                if (top != null && top.Context != null)
                    top.Context.blackboard[done.completeRequestKey] =
                        done.completeRequestValue.entryName ?? "";
            }
            // The asking zone owns the completion when the finished row IS its cursor —
            // then the stack's ORDER is the chain. Everything else is the linear line,
            // where nextOnComplete still speaks.
            ZoneDef zone = FindZoneById(m_ActiveZone);
            if (zone != null && m_ZoneIndex.TryGetValue(m_ActiveZone, out int index)
                && ReferenceEquals(StackRowAt(zone, index), done))
            {
                m_ZoneIndex[m_ActiveZone] = index + 1;
                current = StackRowAt(zone, index + 1);
            }
            else
            {
                ObjectiveDef next = Find(done.nextOnComplete.entryName);
                m_LinearCursor = next;
                current = next;
            }
            Moved();
            SettleGates();
        }

        /// <summary>The current row's gate against the scope board: no gate or agreement
        /// runs; an unset key is PENDING - inert, waiting; a mismatch is passed over by
        /// <see cref="SettleGates"/>.</summary>
        public bool currentPending
        {
            get
            {
                if (current == null)
                    return false;
                string key = current.gateKey;
                if (string.IsNullOrEmpty(key))
                    return false;
                var board = scope != null && scope.Context != null ? scope.Context.blackboard : null;
                return board == null || !board.ContainsKey(key);
            }
        }

        /// <summary>The gate step, callable without a frame - tests drive it.</summary>
        public void SettleNow()
        {
            SettleGates();
        }

        /// <summary>Pass over every current row whose gate is set and DISAGREES - silently:
        /// a passed row never completed, so nothing announces and nothing is called.</summary>
        private void SettleGates()
        {
            for (int guard = 0; guard < 64 && current != null; guard++)
            {
                ObjectiveDef row = current;
                string key = row.gateKey;
                if (string.IsNullOrEmpty(key))
                    return;
                var board = scope != null && scope.Context != null ? scope.Context.blackboard : null;
                if (board == null || !board.TryGetValue(key, out object held))
                    return;   // pending - the ledger waits
                if (GateAgrees(held, row.gateValue))
                    return;   // the gate agrees - the row runs
                PassOver(row);
                if (ReferenceEquals(current, row))
                    return;
            }
        }

        /// <summary>The board's number, however it was boxed - the same tolerance the
        /// compare condition reads with.</summary>
        private static bool GateAgrees(object held, float wanted)
        {
            switch (held)
            {
                case float f: return Mathf.Approximately(f, wanted);
                case int i: return Mathf.Approximately(i, wanted);
                case double d: return Mathf.Approximately((float)d, wanted);
                case string s: return float.TryParse(s,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float parsed)
                    && Mathf.Approximately(parsed, wanted);
                default: return false;
            }
        }

        /// <summary>Advance past a row the way Complete does, minus everything a completion
        /// means: it just was not this playthrough.</summary>
        private void PassOver(ObjectiveDef row)
        {
            ZoneDef zone = FindZoneById(m_ActiveZone);
            if (zone != null && m_ZoneIndex.TryGetValue(m_ActiveZone, out int index)
                && ReferenceEquals(StackRowAt(zone, index), row))
            {
                m_ZoneIndex[m_ActiveZone] = index + 1;
                current = StackRowAt(zone, index + 1);
            }
            else
            {
                ObjectiveDef next = Find(row.nextOnComplete.entryName);
                m_LinearCursor = next;
                current = next;
            }
            progress = 0;
            Moved();
        }

        /// <summary>Complete a row BY NAME: the current one, or a released zone's cursor -
        /// the ledger advances either way, which is what lets a film state that pre-empted
        /// the zone still say "that step is done". A name that is neither is a stale write
        /// and completes nothing.</summary>
        public void CompleteRow(string rowName)
        {
            if (current != null && string.Equals(current.name, rowName, StringComparison.Ordinal))
            {
                Complete();
                return;
            }
            IndexZones();
            ZoneRegistry zones = Zones();
            for (int i = 0; zones != null && i < zones.entries.Count; i++)
            {
                ZoneDef zone = zones.entries[i];
                if (zone == null || string.IsNullOrEmpty(zone.id)
                    || !m_ZoneIndex.TryGetValue(zone.id, out int index))
                    continue;
                ObjectiveDef cursor = StackRowAt(zone, index);
                if (cursor == null
                    || !string.Equals(cursor.name, rowName, StringComparison.Ordinal))
                    continue;
                m_ZoneIndex[zone.id] = index + 1;
                Announce(CompletedAnnouncement, rowName);
                Knock();
                return;
            }
        }

        /// <summary>Every declared zone starts at the top of its stack. The container row
        /// IS the authoring surface: adding the next task to a zone is appending to its
        /// list — no chain wiring, no membership fields.</summary>
        private void IndexZones()
        {
            if (m_ZonesIndexed)
                return;
            ZoneRegistry zones = Zones();
            if (catalog == null)
                return;
            m_ZonesIndexed = true;
            for (int i = 0; zones != null && i < zones.entries.Count; i++)
            {
                ZoneDef zone = zones.entries[i];
                if (zone == null || string.IsNullOrEmpty(zone.id)
                    || zone.asset == null || zone.asset.entries.Count == 0)
                    continue;
                if (!m_ZoneIndex.ContainsKey(zone.id))
                    m_ZoneIndex.Add(zone.id, 0);
            }
        }

        /// <summary>
        /// THE DISTANCE-ZONE SWITCH (the HT orchestrator, row-shaped): among zones that
        /// still have work, the NEAREST placed volume to the player wins, and its stack's
        /// cursor becomes current — walking changes what is asked. No competing zone (none
        /// placed, all done) falls back to the linear line, which keeps its own place.
        /// </summary>
        private void OrchestrateZones()
        {
            // A tree's ask outranks distance: while one stands, walking changes nothing.
            if (!string.IsNullOrEmpty(m_AskedZone))
                return;
            IndexZones();
            if (m_ZoneIndex.Count == 0)
                return;
            StateTreeContextHost player =
                StateTreeContextHost.Resolve(scope.gameObject, StateTreeContextKind.Player);
            if (player == null)
                return;

            string bestZone = null;
            var bestDistance = float.MaxValue;
            foreach (KeyValuePair<string, int> pair in m_ZoneIndex)
            {
                ZoneDef zone = FindZoneById(pair.Key);
                if (zone == null || zone.asset == null || pair.Value >= zone.asset.entries.Count)
                    continue;   // this zone's stack is done — it stopped competing
                WorldObjectBehaviour volume = Nearest(pair.Key, player.transform.position);
                if (volume == null)
                    continue;   // no volume placed in THIS level — not a candidate here
                Vector3 offset = volume.transform.position - player.transform.position;
                offset.y = 0f;
                float distance = offset.sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestZone = pair.Key;
                }
            }

            if (bestZone == null)
            {
                // Nothing competes: the linear line resumes exactly where it stood.
                if (!string.IsNullOrEmpty(m_ActiveZone))
                {
                    m_ActiveZone = "";
                    if (!ReferenceEquals(current, m_LinearCursor))
                    {
                        current = m_LinearCursor;
                        progress = 0;
                        Moved();
                    }
                }
                return;
            }

            ObjectiveDef cursor = StackRowAt(FindZoneById(bestZone), m_ZoneIndex[bestZone]);
            if (string.Equals(bestZone, m_ActiveZone, StringComparison.Ordinal)
                && ReferenceEquals(cursor, current))
                return;

            m_ActiveZone = bestZone;
            if (!ReferenceEquals(cursor, current))
            {
                current = cursor;
                progress = 0;
                Moved();
            }
        }

        // ---- the report surface: the game's facts arrive here -------------------------

        /// <summary>Somebody the game counts as an enemy went down. Filtered by the row's
        /// tag when it has one; the victim rides along so the filter can ask it.</summary>
        public void ReportKill(WorldObjectBehaviour victim)
        {
            if (current == null || current.kind != ObjectiveKind.EnemyKill || currentPending)
                return;
            if (!string.IsNullOrEmpty(current.targetTag)
                && (victim == null || !victim.HasTag(current.targetTag)))
                return;
            progress += 1;
            Knock();
            RefreshBanner();
            if (progress >= Mathf.Max(1, current.count))
                Complete();
        }

        /// <summary>How many of an item the player carries NOW — absolute, not a delta, so
        /// dropping an item honestly un-progresses the objective.</summary>
        public void ReportPickupCount(string itemName, int carried)
        {
            if (current == null || current.kind != ObjectiveKind.Pickup || currentPending)
                return;
            if (!string.Equals(current.target.entryName, itemName, StringComparison.Ordinal))
                return;
            int goal = Mathf.Max(1, current.count);
            int clamped = Mathf.Clamp(carried, 0, goal);
            if (clamped != progress)
            {
                progress = clamped;
                Knock();
                RefreshBanner();
            }
            if (carried >= goal)
                Complete();
        }

        /// <summary>A conversation finished — by the dialog ROW's name, the registry key.</summary>
        public void ReportDialogFinished(string dialogRowName)
        {
            if (current == null || current.kind != ObjectiveKind.Dialog || currentPending)
                return;
            if (string.Equals(current.target.entryName, dialogRowName, StringComparison.Ordinal))
                Complete();
        }

        // ---- what the toolset checks for itself ---------------------------------------

        protected override void OnTick(float deltaTime)
        {
            if (!m_AutoStarted)
            {
                if (string.IsNullOrEmpty(startingObjective.entryName))
                {
                    m_AutoStarted = true;
                }
                else
                {
                    ObjectiveDef row = Find(startingObjective.entryName);
                    if (row != null)
                    {
                        m_AutoStarted = true;
                        m_LinearCursor = row;
                        if (current == null)
                            Activate(row);
                    }
                }
            }

            OrchestrateZones();
            SettleGates();

            if (current != null && current.kind == ObjectiveKind.MoveTo)
                CheckArrival();
            CheckFacts();
            PlaceMarker();
        }

        /// <summary>The FACT watcher: a row naming a board key completes when the scope
        /// board says so. Public so tests drive it without a frame.</summary>
        public void CheckFacts()
        {
            if (current == null || currentPending)
                return;
            string key = current.factKey;
            if (string.IsNullOrEmpty(key))
                return;
            var board = scope != null && scope.Context != null ? scope.Context.blackboard : null;
            if (board == null || !board.TryGetValue(key, out object held))
                return;
            if (string.IsNullOrEmpty(current.factValue) || string.Equals(
                    held != null ? held.ToString() : "", current.factValue, StringComparison.Ordinal))
                Complete();
        }

        /// <summary>The orchestrator's step, callable without a frame — tests drive it.</summary>
        public void OrchestrateNow()
        {
            OrchestrateZones();
        }

        /// <summary>Everything a reload needs, as row names.</summary>
        public SaveState CaptureState()
        {
            IndexZones();
            var state = new SaveState { hasState = true };
            foreach (KeyValuePair<string, int> pair in m_ZoneIndex)
            {
                ObjectiveDef cursor = StackRowAt(FindZoneById(pair.Key), pair.Value);
                state.zoneNames.Add(pair.Key);
                state.zoneCursors.Add(cursor != null ? cursor.name : "");
            }
            state.linearCursor = m_LinearCursor != null ? m_LinearCursor.name : "";
            state.currentName = current != null ? current.name : "";
            state.progress = progress;
            return state;
        }

        /// <summary>Resume a saved line: cursors land where they stood, done stacks stay
        /// done, and auto-start stands down — the save IS the start.</summary>
        public void RestoreState(SaveState state)
        {
            if (state == null || !state.hasState)
                return;
            IndexZones();
            m_AutoStarted = true;
            for (int i = 0; i < state.zoneNames.Count; i++)
            {
                ZoneDef zone = FindZoneById(state.zoneNames[i]);
                if (zone == null || zone.asset == null)
                    continue;
                var cursorName = i < state.zoneCursors.Count ? state.zoneCursors[i] : "";
                var index = zone.asset.entries.Count;   // "" = the stack was done
                for (int j = 0; j < zone.asset.entries.Count
                    && !string.IsNullOrEmpty(cursorName); j++)
                {
                    if (zone.asset.entries[j] != null && string.Equals(
                            zone.asset.entries[j].name, cursorName, StringComparison.Ordinal))
                    {
                        index = j;
                        break;
                    }
                }
                m_ZoneIndex[state.zoneNames[i]] = index;
            }
            m_LinearCursor = Find(state.linearCursor);
            current = Find(state.currentName);
            progress = state.progress;
            Moved();
        }

        /// <summary>The MoveTo watcher: nearest zone carrying the row's tag, arrived when
        /// the player stands within its radius (the zone's own wins over the row's).
        /// Public so tests drive it without a frame.</summary>
        public void CheckArrival()
        {
            if (current == null || current.kind != ObjectiveKind.MoveTo || currentPending)
                return;
            StateTreeContextHost player =
                StateTreeContextHost.Resolve(scope.gameObject, StateTreeContextKind.Player);
            if (player == null)
                return;
            WorldObjectBehaviour zone = Nearest(current.targetTag, player.transform.position);
            if (zone == null)
                return;
            float arrive = current.radius;
            var zoneBehaviour = zone.GetComponent<ObjectiveZoneBehaviour>();
            if (zoneBehaviour != null && zoneBehaviour.radius > 0f)
                arrive = zoneBehaviour.radius;
            Vector3 offset = zone.transform.position - player.transform.position;
            offset.y = 0f;
            if (offset.magnitude <= arrive)
                Complete();
        }

        /// <summary>Where the arrow points: the nearest citizen carrying the current row's
        /// tag, from the player's position. Null when the row is silent about place.</summary>
        /// <summary>
        /// EVERY live thing the current row is about, not just the nearest — the list a
        /// pointer per target is drawn from, and the reason a row asking for six raiders can
        /// show six arrows instead of one that keeps changing its mind.
        ///
        /// Order is nearest-first, so a skin that can only show a few shows the ones that
        /// matter. Empty when the row names no tag, when nothing carries it, or when the
        /// world has not registered anything yet — all normal, none an error.
        /// </summary>
        public void CurrentTargets(List<WorldObjectBehaviour> into)
        {
            into?.Clear();
            if (into == null || current == null || string.IsNullOrEmpty(current.targetTag))
                return;
            WorldService world = scope.GetService<WorldService>();
            if (world == null)
                return;

            StateTreeContextHost player =
                StateTreeContextHost.Resolve(scope.gameObject, StateTreeContextKind.Player);
            Vector3 from = player != null ? player.transform.position : scope.transform.position;

            m_Buffer.Clear();
            world.CollectByTag(current.targetTag, m_Buffer);
            for (int i = 0; i < m_Buffer.Count; i++)
            {
                WorldObjectBehaviour candidate = m_Buffer[i];
                if (candidate == null)
                    continue;
                // ONE ENTRY PER BODY. A citizen wears several of these components at once — it
                // is a character AND an npc AND whatever else its prefab says — and each
                // registers itself, so the tag index holds a raider three times over. The
                // nearest-one question never noticed; a list does, and three pointers stacked
                // on one body is exactly the noise the per-target pointers exist to avoid.
                bool already = false;
                for (int j = 0; j < into.Count; j++)
                {
                    if (into[j] != null && into[j].gameObject == candidate.gameObject)
                    {
                        already = true;
                        break;
                    }
                }
                if (!already)
                    into.Add(candidate);
            }
            into.Sort((a, b) =>
                Flat(a.transform.position - from).CompareTo(Flat(b.transform.position - from)));
        }

        private static float Flat(Vector3 offset)
        {
            offset.y = 0f;
            return offset.sqrMagnitude;
        }


        /// <summary>Ask to be shown one of the current row's targets; null means the nearest.
        /// Quiet when the row has none — a tap on a stale pointer should do nothing, loudly
        /// or otherwise.</summary>
        public void Focus(WorldObjectBehaviour target)
        {
            WorldObjectBehaviour shown = target;
            if (shown == null)
            {
                CurrentTargets(m_Targets);
                shown = m_Targets.Count > 0 ? m_Targets[0] : null;
            }
            if (shown == null)
                return;
            // WHAT LOOKING MEANS is the game's (meta-rule 5): asked for once, called here.
            m_Look ??= StateTreeContextHost.FindService<ILookAt>(scope.gameObject);
            m_Look?.LookAt(shown);
        }

        private readonly List<WorldObjectBehaviour> m_Targets = new List<WorldObjectBehaviour>();

        protected override void OnRequest(ServiceRequest request, string value)
        {
            if (request.action == CompleteAction)
            {
                if (string.IsNullOrEmpty(value))
                    Complete();
                else
                    CompleteRow(value);
                return;
            }
            if (request.action != FocusAction)
                return;
            // BY STABLE ID, because the value crosses a blackboard as text: a tree or a skin
            // names WHICH one, and an empty value means "whichever is nearest" — the same
            // answer the arrow gives when it only has room for one.
            WorldObjectBehaviour picked = null;
            if (!string.IsNullOrEmpty(value))
            {
                CurrentTargets(m_Targets);
                for (int i = 0; i < m_Targets.Count; i++)
                {
                    if (m_Targets[i] != null && m_Targets[i].stableId == value)
                    {
                        picked = m_Targets[i];
                        break;
                    }
                }
            }
            Focus(picked);
        }

        public Vector3? CurrentTargetPosition()
        {
            if (current == null || string.IsNullOrEmpty(current.targetTag))
                return null;
            StateTreeContextHost player =
                StateTreeContextHost.Resolve(scope.gameObject, StateTreeContextKind.Player);
            Vector3 from = player != null ? player.transform.position : scope.transform.position;
            WorldObjectBehaviour nearest = Nearest(current.targetTag, from);
            return nearest != null ? nearest.transform.position : (Vector3?)null;
        }

        private WorldObjectBehaviour Nearest(string tag, Vector3 from)
        {
            WorldService world = scope.GetService<WorldService>();
            if (world == null || string.IsNullOrEmpty(tag))
                return null;
            m_Buffer.Clear();
            world.CollectByTag(tag, m_Buffer);
            WorldObjectBehaviour best = null;
            var bestDistance = float.MaxValue;
            for (int i = 0; i < m_Buffer.Count; i++)
            {
                if (m_Buffer[i] == null)
                    continue;
                Vector3 offset = m_Buffer[i].transform.position - from;
                offset.y = 0f;
                float distance = offset.sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = m_Buffer[i];
                }
            }
            return best;
        }
    }
}

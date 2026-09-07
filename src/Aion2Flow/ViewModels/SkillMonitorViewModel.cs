using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.Presentation;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cloris.Aion2Flow.ViewModels;

public sealed class SkillMonitorViewModel : ObservableObject
{
    private const int JournalReadBatchSize = 2_048;
    private readonly WinDivertCaptureService _captureService;
    private readonly GameResourceService _resources;
    private readonly LocalizationService _localization;
    private readonly SettingsService _settings;
    private readonly Predicate<int> _cooldownSelection;
    private readonly PacketCooldownTracker _cooldowns = new();
    private readonly SkillMonitorSkillSlotBuilder _skillSlotBuilder = new();
    private readonly SkillMonitorSkillSlotPresentationTracker _skillSlotPresentationTracker = new();
    private readonly List<AuraInstanceState> _activeAuras = [];
    private readonly List<SkillMonitorAuraCandidate> _auraCandidates = [];
    private readonly List<SkillMonitorSkillSlotData> _skillRowData = [];
    private readonly SkillMonitorSkillRowCollection _skillRows = [];
    private Guid _sessionId;
    private JournalCursor _journalCursor;
    private EncounterTimeDisplayFormat _encounterTimeDisplayFormat;

    public SkillMonitorViewModel(
        WinDivertCaptureService captureService,
        GameResourceService resources,
        LocalizationService localization,
        SettingsService settings)
    {
        _captureService = captureService;
        _resources = resources;
        _localization = localization;
        _settings = settings;
        _cooldownSelection = IsCooldownMonitored;
        _encounterTimeDisplayFormat = settings.Current.EncounterTimeDisplayFormat;
    }

    public ObservableCollection<SkillMonitorSkillSlot> SkillRows => _skillRows;

    public EncounterTimeDisplayFormat EncounterTimeDisplayFormat
    {
        get => _encounterTimeDisplayFormat;
        set => SetProperty(ref _encounterTimeDisplayFormat, value);
    }

    public void ProcessUiFrame()
    {
        var scene = _captureService.Scene;
        if (!_captureService.IsDriverActive)
        {
            if (_skillRows.Count != 0)
                _skillRows.Clear();
            _skillSlotPresentationTracker.Clear();
            return;
        }

        var frame = scene.CopyLocalPlayerAurasTo(_activeAuras);
        EnsureSession(in frame);
        ReadCooldownJournal(scene.Journal, in frame);
        RefreshSkillRows(CollectionsMarshal.AsSpan(_activeAuras), frame.EntityId, frame.ObservedAtMilliseconds);
    }

    private void EnsureSession(in SceneLocalPlayerFrame frame)
    {
        if (_sessionId == frame.SessionId)
            return;

        _sessionId = frame.SessionId;
        _cooldowns.Clear();
        _skillSlotPresentationTracker.Clear();
        _journalCursor = new JournalCursor(frame.StartObservationOrdinal);
    }

    private void ReadCooldownJournal(ObservedEventJournal journal, in SceneLocalPlayerFrame frame)
    {
        var localPlayerEntityId = frame.EntityId;

        while (_journalCursor.NextObservationOrdinal < frame.EndObservationOrdinalExclusive)
        {
            var result = journal.ReadEntries(_journalCursor, frame.EndObservationOrdinalExclusive, JournalReadBatchSize, entries =>
            {
                for (var index = 0; index < entries.Count; index++)
                {
                    var entry = entries[index];
                    if (entry.Domain == ObservedEventDomain.Combat)
                    {
                        var raw = entry.Raw;
                        var combat = entry.Combat;
                        ObserveSkillPacket(
                            entry.ObservedAtMilliseconds,
                            entry.SourceEntityId,
                            localPlayerEntityId,
                            raw.Opcode,
                            in combat);
                    }
                    else if (entry.Domain == ObservedEventDomain.State && entry.State.StateCode == StateCodes.CooldownStart0238)
                    {
                        ObserveCooldownStart(
                            entry.ObservedAtMilliseconds,
                            entry.SourceEntityId,
                            localPlayerEntityId,
                            entry.State.Value0,
                            entry.State.Value1,
                            entry.State.DetailRaw);
                    }
                    else if (entry.Domain == ObservedEventDomain.State && entry.State.StateCode == StateCodes.Cooldown4738)
                    {
                        ObserveCooldownUpdate(
                            entry.ObservedAtMilliseconds,
                            entry.State.Value0,
                            entry.State.Value1);
                    }
                    else if (entry.Domain == ObservedEventDomain.State && entry.State.StateCode == StateCodes.CooldownCharge2238)
                    {
                        ObserveCooldownCharge(
                            entry.ObservedAtMilliseconds,
                            entry.State.Value0,
                            entry.State.Value1,
                            entry.State.DetailRaw);
                    }
                }
            });

            _journalCursor = result.Cursor;
            if (result.Count == 0)
                return;
        }
    }

    private bool ObserveCooldownStart(
        long observedAtMilliseconds,
        int sourceEntityId,
        int localPlayerEntityId,
        long skillValue,
        long remainingValue,
        long detailRaw)
    {
        if (skillValue is <= 0 or > int.MaxValue || remainingValue is <= 0 or > int.MaxValue)
        {
            return false;
        }
        if (!CooldownStartObservationDetail.TryDecode(detailRaw, out _, out var availableCountAfterControl))
            return false;

        var packetSkillCode = (int)skillValue;
        if (!TryNormalizePacketSkillCode(packetSkillCode, out var normalizedSkillId))
            return false;
        return _cooldowns.ObserveStart0238(
            localPlayerEntityId,
            sourceEntityId,
            normalizedSkillId,
            packetSkillCode,
            (int)remainingValue,
            observedAtMilliseconds,
            _resources.ResolveMaxAvailableCount(packetSkillCode),
            availableCountAfterControl);
    }

    private bool ObserveCooldownUpdate(long observedAtMilliseconds, long rowBaseSkillValue, long remainingValue)
    {
        if (rowBaseSkillValue is <= 0 or > int.MaxValue || remainingValue is < 0 or > int.MaxValue)
            return false;

        var packetSkillCode = (int)rowBaseSkillValue;
        if (!TryNormalizePacketSkillCode(packetSkillCode, out var rowBaseSkillId))
            return false;
        return _cooldowns.ObserveUpdate4738(
            rowBaseSkillId,
            packetSkillCode,
            (int)remainingValue,
            observedAtMilliseconds,
            _resources.ResolveMaxAvailableCount(packetSkillCode));
    }

    private bool ObserveCooldownCharge(
        long observedAtMilliseconds,
        long skillValue,
        long remainingValue,
        long detailRaw)
    {
        if (skillValue is <= 0 or > int.MaxValue || remainingValue is < 0 or > int.MaxValue ||
            !CooldownChargeObservationDetail.TryDecode(detailRaw, out _, out var availableCount))
        {
            return false;
        }

        var packetSkillCode = (int)skillValue;
        if (!TryNormalizePacketSkillCode(packetSkillCode, out var rowBaseSkillId))
            return false;
        return _cooldowns.ObserveCharge2238(
            rowBaseSkillId,
            packetSkillCode,
            availableCount,
            (int)remainingValue,
            observedAtMilliseconds);
    }

    private bool ObserveSkillPacket(
        long observedAtMilliseconds,
        int sourceEntityId,
        int localPlayerEntityId,
        ushort opcode,
        in CombatWireObservation combat)
    {
        if (!PacketCooldownSourceFilter.MatchesKnownLocalPlayer(localPlayerEntityId, sourceEntityId) ||
            opcode is not (0x0238 or 0x0438))
            return false;

        var hasSkillCode = combat.SkillCode > 0 || combat.BodySkillVariantRaw > 0 || combat.BodyCodeRaw > 0;
        if (!hasSkillCode)
            return false;

        var packetSkillCode = combat.SkillCode > 0
            ? combat.SkillCode
            : combat.BodySkillVariantRaw > 0
                ? combat.BodySkillVariantRaw
                : combat.BodyCodeRaw <= int.MaxValue
                    ? (int)combat.BodyCodeRaw
                    : 0;
        if (!TryNormalizePacketSkillCode(packetSkillCode, out var rowBaseSkillId))
            return false;

        return opcode == 0x0238 &&
            _cooldowns.ObserveControl0238(
                rowBaseSkillId,
                packetSkillCode,
                sourceEntityId,
                observedAtMilliseconds,
                _resources.ResolveMaxAvailableCount(packetSkillCode));
    }

    private bool TryNormalizePacketSkillCode(int packetSkillCode, out int normalizedSkillId)
    {
        if (packetSkillCode <= 0)
        {
            normalizedSkillId = 0;
            return false;
        }

        var skillId = packetSkillCode;
        if (!_resources.ContainsSkill(packetSkillCode) &&
            CombatResourceRegistry.TryResolveSkillIdByEffectRef(
                ResourceEffectRef.FromRaw(unchecked((uint)packetSkillCode)),
                out var effectSkillId))
        {
            skillId = effectSkillId;
        }

        normalizedSkillId = _resources.ResolveBaseSkillIdForCode(skillId);
        return normalizedSkillId > 0;
    }

    private void RefreshSkillRows(ReadOnlySpan<AuraInstanceState> auras, int localPlayerEntityId, long nowMilliseconds)
    {
        _auraCandidates.Clear();
        for (var index = 0; index < auras.Length; index++)
        {
            var aura = auras[index];
            if (!TryResolveAuraSkillId(aura.ResourceEffectRef, out var rowBaseSkillId))
                continue;
            if (!IsBuffMonitored(rowBaseSkillId))
                continue;

            _auraCandidates.Add(new SkillMonitorAuraCandidate(
                aura.TargetEntityId,
                rowBaseSkillId,
                aura.DurationMilliseconds,
                aura.ExpiresAtMilliseconds));
        }

        var slots = _skillSlotBuilder.Build(
            CollectionsMarshal.AsSpan(_auraCandidates),
            localPlayerEntityId,
            _cooldowns.States,
            _cooldownSelection,
            nowMilliseconds);
        var presentations = _skillSlotPresentationTracker.Update(
            slots,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        _skillRowData.Clear();
        for (var index = 0; index < presentations.Length; index++)
        {
            var presentation = presentations[index];
            var slot = presentation.Slot;
            var buffRemainingText = slot.BuffTimer is { } buffTimer ? FormatTimer(in buffTimer) : string.Empty;
            var cooldownRemainingText = slot.CooldownTimer is { } cooldownTimer ? FormatTimer(in cooldownTimer) : string.Empty;
            _skillRowData.Add(new SkillMonitorSkillSlotData(
                slot.RowBaseSkillId,
                _resources.ResolveSkillIconAssetName(slot.RowBaseSkillId),
                CreateSkillToolTip(in slot, buffRemainingText, cooldownRemainingText),
                slot.BuffTimer?.ProgressValue ?? 0d,
                slot.BuffTimer is not null,
                buffRemainingText,
                slot.CooldownTimer?.ProgressValue ?? 0d,
                cooldownRemainingText,
                slot.CooldownTimer is not null,
                slot.AvailableCount,
                slot.AvailableCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                slot.AvailableCount is not null,
                presentation.CompletionStartedUtcMilliseconds));
        }

        _skillRows.Reconcile(CollectionsMarshal.AsSpan(_skillRowData));
    }

    private bool IsBuffMonitored(int rowBaseSkillId)
        => SkillMonitorSelection.IncludesBuff(_settings.Current, _resources, rowBaseSkillId);

    private bool IsCooldownMonitored(int rowBaseSkillId)
        => SkillMonitorSelection.IncludesCooldown(_settings.Current, _resources, rowBaseSkillId);

    private string CreateSkillToolTip(
        in SkillMonitorSkillSlotState slot,
        string buffRemainingText,
        string cooldownRemainingText)
    {
        var name = ResolveLocalizedSkillName(slot.RowBaseSkillId);
        if (slot.BuffTimer is null)
            return slot.CooldownTimer is not null
                ? $"{name}\n{_localization["SkillMonitor_Buffs"]}: -\n{_localization["SkillMonitor_Cooldowns"]}: {cooldownRemainingText}"
                : name;

        if (slot.CooldownTimer is null)
            return $"{name}\n{_localization["SkillMonitor_Buffs"]}: {buffRemainingText}\n{_localization["SkillMonitor_Cooldowns"]}: -";

        return $"{name}\n{_localization["SkillMonitor_Buffs"]}: {buffRemainingText}\n{_localization["SkillMonitor_Cooldowns"]}: {cooldownRemainingText}";
    }

    private bool TryResolveAuraSkillId(ResourceEffectRef effectRef, out int normalizedSkillId)
    {
        normalizedSkillId = 0;
        if (effectRef.IsEmpty)
        {
            return false;
        }

        if (CombatResourceRegistry.TryResolveSkillIdByEffectRef(effectRef, out var ownerSkillId))
        {
            normalizedSkillId = _resources.ResolveBaseSkillIdForCode(ownerSkillId);
            if (normalizedSkillId > 0)
            {
                return true;
            }
        }

        if (CombatResourceRegistry.TryResolveAuraResourceSemantics(effectRef, out var resolution) &&
            resolution.Slot is { SkillId: > 0 } slot)
        {
            normalizedSkillId = _resources.ResolveBaseSkillIdForCode(slot.SkillId);
            if (normalizedSkillId > 0)
            {
                return true;
            }
        }

        if (effectRef.RawId <= int.MaxValue && _resources.ContainsSkill((int)effectRef.RawId))
        {
            normalizedSkillId = _resources.ResolveBaseSkillIdForCode((int)effectRef.RawId);
            return normalizedSkillId > 0;
        }

        normalizedSkillId = 0;
        return false;
    }

    private string ResolveLocalizedSkillName(int skillId)
        => TryResolveLocalizedSkillName(skillId, out var name)
            ? name
            : _localization["SkillMonitor_UnknownSkill"];

    private bool TryResolveLocalizedSkillName(int skillId, out string name)
    {
        if (skillId <= 0)
        {
            name = string.Empty;
            return false;
        }

        var rowBaseSkillId = _resources.ResolveBaseSkillIdForCode(skillId);
        name = _resources.ResolveSkillName(rowBaseSkillId);
        if (string.IsNullOrWhiteSpace(name) ||
            string.Equals(name, rowBaseSkillId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            name = string.Empty;
            return false;
        }

        return true;
    }

    private string FormatDuration(long milliseconds)
        => SkillMonitorTimeFormatter.Format(milliseconds, EncounterTimeDisplayFormat);

    private string FormatTimer(in SkillMonitorTimer timer)
        => timer.IsIndefinite ? _localization["SkillMonitor_Indefinite"] : FormatDuration(timer.RemainingMilliseconds);

}

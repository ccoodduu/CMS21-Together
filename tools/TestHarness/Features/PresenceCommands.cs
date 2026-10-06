using System;
using System.Globalization;
using System.Linq;
using CMS21Together.Data;
using CMS21Together.Logic.Player;
using UnityEngine;

namespace TogetherTestHarness.Features;

public static class PresenceCommands
{
    [HarnessCommand("teleport")]
    private static object Teleport(string args)
    {
        var values = (args ?? "").Split(',').Select(v => float.Parse(v.Trim(), CultureInfo.InvariantCulture)).ToArray();
        if (values.Length < 3) throw new ArgumentException("usage: teleport x,y,z[,yaw]");
        if (!PresenceManager.HasLocalMotor) throw new InvalidOperationException("no local player in this scene");

        var motor = PresenceManager.LocalMotor;
        var controller = motor.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        motor.transform.position = new Vector3(values[0], values[1], values[2]);
        if (values.Length > 3) motor.transform.rotation = Quaternion.Euler(0f, values[3], 0f);
        if (controller != null) controller.enabled = wasEnabled;

        Movement.ForceSend();
        return $"teleported to {motor.transform.position}";
    }

    [HarnessCommand("stand-before")]
    private static object StandBefore(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new ArgumentException("usage: stand-before <name> <metres>");
        var player = PresenceManager.Roster.Values.FirstOrDefault(p => p.Record.Username == parts[0]);
        if (player == null || !player.HasAvatar) throw new ArgumentException($"no visible player '{parts[0]}'");
        if (!PresenceManager.HasLocalMotor || Camera.main == null) throw new InvalidOperationException("no local player or camera");

        float distance = float.Parse(parts[1], CultureInfo.InvariantCulture);
        Vector3 forward = Camera.main.transform.forward;
        forward.y = 0f;
        forward.Normalize();

        var motor = PresenceManager.LocalMotor;
        Vector3 target = player.Avatar.transform.position - forward * distance;
        target.y = motor.transform.position.y;

        var controller = motor.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        motor.transform.position = target;
        if (controller != null) controller.enabled = wasEnabled;

        Movement.ForceSend();
        return $"standing {distance} m before {parts[0]}";
    }

    [HarnessCommand("set-name")]
    private static object SetName(string args)
    {
        PlayerSettings.NameOverride = args?.Trim();
        return $"name '{PlayerSettings.PlayerName}'";
    }
}

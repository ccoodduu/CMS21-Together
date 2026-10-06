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

    [HarnessCommand("set-name")]
    private static object SetName(string args)
    {
        PlayerSettings.NameOverride = args?.Trim();
        return $"name '{PlayerSettings.PlayerName}'";
    }
}

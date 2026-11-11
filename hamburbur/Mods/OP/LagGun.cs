using ExitGames.Client.Photon;
using hamburbur.Libs;
using hamburbur.Mod_Backend;
using hamburbur.Tools;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace hamburbur.Mods.OP;

[hamburburmod(                "Kick Gun[M]", "Kicks whoever your hand desires", ButtonType.Togglable, AccessSetting.Public,
        EnabledType.Disabled, 0)]
public class LagGun : hamburburmod
{
    private static   float  eventDelay;
    private readonly GunLib gunLib = new() { ShouldFollow = true, };

    protected override void Start() => gunLib.Start();

  protected override void LateUpdate()
{
    gunLib.LateUpdate();

    if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
        return;

    if (!gunLib.IsShooting ||
        gunLib.ChosenRig == null ||
        Time.time <= eventDelay)
        return;

    int targetActor = gunLib.ChosenRig.GetNetPlayer().ActorNumber;

    RaiseEventOptions raiseEventOptions = new()
    {
        TargetActors = [targetActor],
        CachingOption = EventCaching.DoNotCache,
    };

    // Photon PUN event 203: CloseConnection.
    // The targeted client is instructed by the server to disconnect.
    bool queued = PhotonNetwork.NetworkingClient.OpRaiseEvent(
        203,
        null,
        raiseEventOptions,
        SendOptions.SendReliable
    );

    if (queued)
        eventDelay = Time.time + 8.5f;
}

    protected override void OnDisable() => gunLib.OnDisable();
}

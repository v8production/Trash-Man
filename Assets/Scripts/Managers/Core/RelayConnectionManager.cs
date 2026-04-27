using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

public class RelayConnectionManager
{
    public const string DefaultConnectionType = "dtls";

    public async Task InitAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    public async Task<string> StartHostAsync(
        NetworkManager networkManager,
        UnityTransport transport,
        int maxClientConnections)
    {
        if (networkManager == null)
            throw new ArgumentNullException(nameof(networkManager));

        if (transport == null)
            throw new ArgumentNullException(nameof(transport));

        await InitAsync();

        Allocation allocation =
            await RelayService.Instance.CreateAllocationAsync(maxClientConnections);

        string relayJoinCode =
            await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        transport.SetRelayServerData(
            AllocationUtils.ToRelayServerData(allocation, DefaultConnectionType));

        if (!networkManager.StartHost())
            throw new InvalidOperationException("NetworkManager.StartHost() failed after Relay allocation.");

        return relayJoinCode;
    }

    public async Task StartClientAsync(
        NetworkManager networkManager,
        UnityTransport transport,
        string relayJoinCode)
    {
        if (networkManager == null)
            throw new ArgumentNullException(nameof(networkManager));

        if (transport == null)
            throw new ArgumentNullException(nameof(transport));

        if (string.IsNullOrWhiteSpace(relayJoinCode))
            throw new ArgumentException("Relay join code is empty.", nameof(relayJoinCode));

        await InitAsync();

        JoinAllocation allocation =
            await RelayService.Instance.JoinAllocationAsync(relayJoinCode);

        transport.SetRelayServerData(
            AllocationUtils.ToRelayServerData(allocation, DefaultConnectionType));

        if (!networkManager.StartClient())
            throw new InvalidOperationException("NetworkManager.StartClient() failed after Relay allocation.");
    }
}
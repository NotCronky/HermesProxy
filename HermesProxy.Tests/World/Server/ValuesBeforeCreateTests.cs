using System;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// The V3_4_3 Values filter reads a batch in the order the client applies it. cMangos sends an item
/// it has just put in a currency-token slot as a Values entry followed by the item's CreateObject,
/// in one SMSG_UPDATE_OBJECT. The client cannot apply the Values entry to an object it does not
/// have yet and answers it with CMSG_OBJECT_UPDATE_FAILED; the create after it carries the fields.
/// </summary>
/// <remarks>
/// <c>ModernVersion.Build</c> is fixed for the test process, so the filter's V3_4_3 arm is reached
/// through <c>UpdateObject.ForceV343ForTests</c>.
/// </remarks>
[Collection("V343ValuesFilter")]
public sealed class ValuesBeforeCreateTests
{
    private static readonly WowGuid128 Player = WowGuid128.Create(HighGuidType703.Player, 15);
    private static readonly WowGuid128 Token = WowGuid128.Create(HighGuidType703.Item, 655);

    private static GameSessionData SessionWithPlayer()
    {
        var state = GameSessionData.CreateNewGameSessionData(null!);
        state.CurrentPlayerGuid = Player;
        state.CurrentMapId = 1;
        return state;
    }

    private static ObjectUpdate StackOf(WowGuid128 guid, uint count)
    {
        var update = new ObjectUpdate(guid, UpdateTypeModern.Values, null!);
        update.ItemData = new ItemData { StackCount = count };
        return update;
    }

    private static void WithV343(Action body)
    {
        UpdateObject.ForceV343ForTests = true;
        try
        {
            body();
        }
        finally
        {
            UpdateObject.ForceV343ForTests = null;
        }
    }

    [Fact]
    public void ValuesAheadOfTheCreate_AreDropped_ValuesAfterItAreKept()
    {
        WithV343(() =>
        {
            var state = SessionWithPlayer();
            var batch = new UpdateObject(state);
            var ahead = StackOf(Token, 1);
            var create = new ObjectUpdate(Token, UpdateTypeModern.CreateObject1, null!);
            var after = StackOf(Token, 2);
            batch.ObjectUpdates.Add(ahead);
            batch.ObjectUpdates.Add(create);
            batch.ObjectUpdates.Add(after);

            UpdateObject.FilterV3_4_3Values(batch, state);

            Assert.Equal([create, after], batch.ObjectUpdates);
            Assert.Contains(Token, state.ClientKnownGuids);
        });
    }

    /// <summary>An object the client already has keeps a Values entry wherever it stands.</summary>
    [Fact]
    public void ValuesAheadOfACreate_OfAnObjectTheClientHas_AreKept()
    {
        WithV343(() =>
        {
            var state = SessionWithPlayer();
            state.ClientKnownGuids.Add(Token);
            var batch = new UpdateObject(state);
            var ahead = StackOf(Token, 1);
            var create = new ObjectUpdate(Token, UpdateTypeModern.CreateObject1, null!);
            batch.ObjectUpdates.Add(ahead);
            batch.ObjectUpdates.Add(create);

            UpdateObject.FilterV3_4_3Values(batch, state);

            Assert.Equal([ahead, create], batch.ObjectUpdates);
        });
    }
}

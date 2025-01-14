﻿using Phantasma.Core;
using Phantasma.Core.Context;

namespace Phantasma.Business.Contracts
{
    public sealed class FriendsContract : NativeContract
    {
        public override NativeContractKind Kind => NativeContractKind.Friends;

        public static readonly int FRIEND_LIMIT_PER_ACCOUNT = 100;

#pragma warning disable 0649
        internal StorageMap _friendMap;
#pragma warning restore 0649

        #region FRIENDLIST
        public void AddFriend(Address target, Address friend)
        {
            Runtime.Expect(Runtime.IsWitness(target), "invalid witness");

            Runtime.Expect(friend.IsUser, "friend must be user addres");
            Runtime.Expect(friend != target, "friend must be different from target address");

            var friendList = _friendMap.Get<Address, StorageList>(target);
            Runtime.Expect(friendList.Count() < FRIEND_LIMIT_PER_ACCOUNT, "friend limit reached");
            Runtime.Expect(!friendList.Contains(friend), "already is friend");

            friendList.Add(friend);

            Runtime.Notify(EventKind.AddressLink, target, friend);
        }

        public void RemoveFriend(Address target, Address friend)
        {
            Runtime.Expect(Runtime.IsWitness(target), "invalid witness");

            var friendList = _friendMap.Get<Address, StorageList>(target);

            Runtime.Expect(friendList.Contains(friend), "friend not found");
            friendList.Remove(friend);

            Runtime.Notify(EventKind.AddressUnlink, target, friend);
        }

        public Address[] GetFriends(Address target)
        {
            var friendList = _friendMap.Get<Address, StorageList>(target);
            return friendList.All<Address>();
        }
        #endregion

    }
}

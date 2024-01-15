using System;
using System.Collections.Generic;
using SCommon;
using Global;

namespace PlayServer
{
    public class CFriendAgent
    {
        private CUser m_Owner;


        private Dictionary<long , _FriendData> m_FriendList = new Dictionary<long , _FriendData>();
        private Dictionary<long , _FriendData> m_ReceivePendingList = new Dictionary<long , _FriendData>();
        private Dictionary<long , _FriendData> m_SendPendingList = new Dictionary<long , _FriendData>();
        
        public void Init(List<_FriendData> friendList, List<_FriendData> recvList, List<_FriendData> sendList)
        {
            m_FriendList.Clear();
            foreach(var data in friendList)
            {
                m_FriendList.Add(data.m_AccountID , data);
            }

            m_ReceivePendingList.Clear();
            foreach (var data in recvList)
            {
                m_ReceivePendingList.Add(data.m_AccountID , data);
            }

            m_SendPendingList.Clear();
            foreach (var data in sendList)
            {
                m_SendPendingList.Add(data.m_AccountID , data);
            }
        }

        public CFriendAgent(CUser owner)
        {
            m_Owner = owner;
        }

        public _FriendData FindFriend(Int64 accountID)
        {
            if (m_FriendList.TryGetValue(accountID, out _FriendData retval))
                return retval;

            return null;
        }

        public List<_FriendData> GetFriendList()
        {
            return new List<_FriendData>(m_FriendList.Values);
        }


        public _FriendData FindSendUser(Int64 accountID)
        {
            if (m_SendPendingList.TryGetValue(accountID, out _FriendData retval))
                return retval;

            return null;
        }

        public List<_FriendData> GetSendPendingList()
        {
            return new List<_FriendData>(m_SendPendingList.Values);
        }

        public _FriendData FindReceiveUser(Int64 accountID)
        {
            if (m_ReceivePendingList.TryGetValue(accountID, out _FriendData retval))
                return retval;

            return null;
        }

        public List<_FriendData> GetRecvPendingList()
        {
            return new List<_FriendData>(m_ReceivePendingList.Values);
        }


        public void ReportFriendAccept(_FriendData friendData , bool isAccept)
        {
            if (FindFriend(friendData.m_AccountID) != null)
                return;

            if (isAccept)
                m_FriendList.Add(friendData.m_AccountID , friendData);

            if (m_ReceivePendingList.ContainsKey(friendData.m_AccountID))
                m_ReceivePendingList.Remove(friendData.m_AccountID);

            if (m_SendPendingList.ContainsKey(friendData.m_AccountID))
                m_SendPendingList.Remove(friendData.m_AccountID);

            //report
            CNetManager.Instance.P2C_ReportFriendAccept(m_Owner.SessionKey , friendData , isAccept);
        }

        public void ReqBeRegisteredFriend(_FriendData friendData , bool isRegister)
        {
            if (isRegister)
            {
                if (FindReceiveUser(friendData.m_AccountID) != null)
                    return;

                m_ReceivePendingList.Add(friendData.m_AccountID , friendData);
            }
            else
            {
                if (FindReceiveUser(friendData.m_AccountID) == null)
                    return;

                m_ReceivePendingList.Remove(friendData.m_AccountID);
            }

            //report
            CNetManager.Instance.P2C_ReportBeRegisteredFriend(m_Owner.SessionKey , friendData , isRegister);
        }


        public void ReportFriendDelete(_FriendData friendData)
        {
            if (m_FriendList.Remove(friendData.m_AccountID))
                CNetManager.Instance.P2C_ReportFriendDelete(m_Owner.SessionKey, friendData);
        }

        public Packet_Result.Result ReqFriendDelete(Int64 targetID)
        {
            if (!m_FriendList.Remove(targetID))
                return Packet_Result.Result.Friend_FriendDeleted;

            CDBManager.Instance.QueryFriendDelete(m_Owner.DBGUID , m_Owner.SessionKey, m_Owner.AccountID, targetID);
            return Packet_Result.Result.Success;
        }

        public void AfterQueryFriendDelete(_FriendData friendData, Packet_Result.Result result)
        {
            CNetManager.Instance.P2C_ResultFriendDelete(m_Owner.SessionKey , result , friendData);
        }

        public Packet_Result.Result ReqSendFriendRegister(long sessionKey , Int64 targetID , bool isRegister)
        {
            //valid
            var friend_data = FindFriend(targetID);
            if (friend_data != null)
                return Packet_Result.Result.Friend_FriendRequest_Error;

            if (targetID == m_Owner.AccountID)
                return Packet_Result.Result.Friend_ToastMessage_14;

            if (isRegister)
            {
                if (m_FriendList.Count >= CDefineTable.Instance.Find(eTableDefineType.Friend_Max_Friend_Count).m_Int64Value)
                    return Packet_Result.Result.Friend_ToastMessage_02;

                if (m_SendPendingList.Count >= CDefineTable.Instance.Find(eTableDefineType.Friend_Max_Friend_Request).m_Int64Value)
                    return Packet_Result.Result.Friend_ToastMessage_04;

                var recv_pending_data = FindReceiveUser(targetID);
                if (recv_pending_data != null)
                    return Packet_Result.Result.Friend_FriendRequest_Error;

                var send_pending_data = FindSendUser(targetID);
                if (send_pending_data != null)
                    return Packet_Result.Result.Friend_FriendRequest_Error;
            }
            else
            {
                var send_pending_data = FindSendUser(targetID);
                if (send_pending_data == null)
                    return Packet_Result.Result.Friend_ToastMessage_14;
            }

            //process
            CDBManager.Instance.QueryFriendRegister(m_Owner.DBGUID , sessionKey , m_Owner.AccountID , targetID, isRegister);
            return Packet_Result.Result.Success;
        }

        public void AfterQueryFriendRegister(_FriendData friendData, bool isRegister, Packet_Result.Result result)
        {
            if (isRegister)
            {
                if (!m_SendPendingList.ContainsKey(friendData.m_AccountID))
                    m_SendPendingList.Add(friendData.m_AccountID, friendData);
            }
            else
            {
                if (m_SendPendingList.ContainsKey(friendData.m_AccountID))
                    m_SendPendingList.Remove(friendData.m_AccountID);
            }

            CNetManager.Instance.P2C_ResultFriendRegister(m_Owner.SessionKey, result, friendData, isRegister);
        }

        public Packet_Result.Result ReqFriendSearch(long sessionKey , string userName)
        {
            if (string.IsNullOrEmpty(userName))
                return Packet_Result.Result.PacketError;

            if (string.Equals(userName, m_Owner.PlayerData.m_Name))
                return Packet_Result.Result.Friend_ToastMessage_14;

            if (!CDefine.CheckValidNickname(userName))
                return Packet_Result.Result.Friend_NotFoundFriend;

            CDBManager.Instance.QueryFrinedSearch(m_Owner.DBGUID, sessionKey , userName);

            return Packet_Result.Result.Success;
        }

        public Packet_Result.Result ReqFriendAccept(long sessionKey, long targetID, bool isAccept)
        {
            var receive_data = FindReceiveUser(targetID);
            if (receive_data == null)
                return Packet_Result.Result.Friend_ToastMessage_14;

            if (isAccept)
            {
                if (m_FriendList.Count >= CDefineTable.Instance.Find(eTableDefineType.Friend_Max_Friend_Count).m_Int64Value)
                    return Packet_Result.Result.Friend_ToastMessage_07;
            }

            //query
            CDBManager.Instance.QueryFriendAccept(m_Owner.DBGUID , sessionKey , m_Owner.AccountID , targetID, isAccept);
            
            return Packet_Result.Result.Success;
        }

        public void AfterQueryFriendAccept(_FriendData friendData, bool isAccept, Packet_Result.Result result)
        {
            if (isAccept)
            {
                if (false == m_FriendList.ContainsKey(friendData.m_AccountID))
                    m_FriendList.Add(friendData.m_AccountID, friendData);
            }

            m_SendPendingList.Remove(friendData.m_AccountID);
            m_ReceivePendingList.Remove(friendData.m_AccountID);
            
            CNetManager.Instance.P2C_ResultFriendAccept(m_Owner.SessionKey , result , friendData, isAccept);
        }

        public void ReqRecvPendingFriendList(List<_FriendData> recvPendingList)
        {
            RefreshRecvPendingList(recvPendingList);
            CNetManager.Instance.P2C_ResultRecvPendingFriendList(m_Owner.SessionKey , Packet_Result.Result.Success , GetRecvPendingList());
        }

        public void ReqSendPendingFriendList(List<_FriendData> sendPendingList)
        {
            RefreshSendPendingList(sendPendingList);
            CNetManager.Instance.P2C_ResultSendPendingFriendList(m_Owner.SessionKey , Packet_Result.Result.Success , GetSendPendingList());
        }

        public void RefreshRecvPendingList(List<_FriendData> recvPendingList)
        {
            m_ReceivePendingList.Clear();
            foreach(var iter in recvPendingList)
                m_ReceivePendingList.Add(iter.m_AccountID , iter);
        }

        public void RefreshSendPendingList(List<_FriendData> sendPendingList)
        {
            m_SendPendingList.Clear();
            foreach(var iter in sendPendingList)
                m_SendPendingList.Add(iter.m_AccountID , iter);
        }

        public void ReqFriendList(List<_FriendData> friendList)
        {
            RefreshFriendList(friendList);
            CNetManager.Instance.P2C_ResultFriendList(m_Owner.SessionKey , Packet_Result.Result.Success , GetFriendList());
        }

        public void RefreshFriendList(List<_FriendData> friendList)
        {
            m_FriendList.Clear();
            foreach(var iter in friendList)
            {
                if (iter.m_UserState == eUserState.Max)
                    iter.m_ForClientLogoutTime = SDateManager.Instance.DatediffSec(iter.m_LogoutTime , DateTime.UtcNow);

                m_FriendList.Add(iter.m_AccountID , iter);
            }
        }
    }
}

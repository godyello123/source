using System;
using System.Collections.Generic;
using SCommon;
using Global;

namespace PlayServer
{
    public class CRune
    {
        public int m_TotalCost;
        public Dictionary<int, _RuneGradeData> m_Gradelist = new Dictionary<int, _RuneGradeData>();
        public Dictionary<int, _RuneCostData> m_Costlist = new Dictionary<int, _RuneCostData>();


        public _RuneData ConvertRuneData()
        {
            _RuneData retVal = new _RuneData();
            retVal.m_TotalCost = m_TotalCost;
            retVal.m_CostData = new List<_RuneCostData>(m_Costlist.Values);
            retVal.m_GradeData = new List<_RuneGradeData>(m_Gradelist.Values);

            return retVal;
        }

        public _RuneGradeData FindRuneGrade(int group_id)
        {
            if (m_Gradelist.TryGetValue(group_id, out _RuneGradeData retval))
                return retval;

            return null;
        }

        public _RuneCostData FindRuneCost(int table_id)
        {
            if (m_Costlist.TryGetValue(table_id, out _RuneCostData retval))
                return retval;

            return null;
        }
    }

    public class CRuneAgent
    {
        private CUser m_Owner;

        private CRune m_Rune = new CRune();

        public CRuneAgent(CUser user)
        {
            m_Owner = user;
        }

        public _RuneData GetRuneData()
        {
            return m_Rune.ConvertRuneData();
        }


        public void Init(List<_RuneGradeData> grade_list, List<_RuneCostData> cost_list)
        {
            var default_cost = CRuneCostTable.Instance.DefaultData();
            foreach (var iter in default_cost)
                m_Rune.m_Costlist.Add(iter.m_TableID, iter);

            var default_grade = CGradeTotalLevelTable.Instance.DefaultData();
            foreach (var iter in default_grade)
                m_Rune.m_Gradelist.Add(iter.m_GroupID, iter);

            foreach (var iter in grade_list)
                m_Rune.m_Gradelist[iter.m_GroupID] = iter;

            foreach (var iter in cost_list)
                m_Rune.m_Costlist[iter.m_TableID] = iter;

            RefreshRuneCost();
        }


        public int GetRuneCost()
        {
            return m_Rune.m_TotalCost;
        }

        public void RefreshRuneCost()
        {
            int basecost = (int)CDefineTable.Instance.Find(eTableDefineType.Rune_BaseMaxCost).m_Int64Value;
            foreach (var iter in m_Rune.m_Costlist)
            {
                var itval = iter.Value;
                if (itval.m_IsRewarded)
                    continue;

                CRuneCostRecord record = CRuneCostTable.Instance.Find(itval.m_TableID);
                if (record == null)
                    continue;

                basecost += record.m_GrowthCost;
            }

            m_Rune.m_TotalCost = basecost;
        }

        public _RuneCostData FindRuneCost(int table_id)
        {
            return m_Rune.FindRuneCost(table_id);
        }

        public _RuneGradeData FindRuneGrade(int group_id)
        {
            return m_Rune.FindRuneGrade(group_id);
        }

        public void GetRuneGradeAbils(ref Dictionary<eAbilBattleType, _AbilData> rAbils)
        {
            foreach (var iter in m_Rune.m_Gradelist)
            {
                var itval = iter.Value;
                CGradeTotalLevelTable.Instance.GetGradeTotalLevelAbils(itval.m_GroupID, itval.m_Level, ref rAbils);
            }
        }

        public Packet_Result.Result ReqItemRuneGradeUnlock(int group_id)
        {
            //valid
            var runeGrade = m_Rune.FindRuneGrade(group_id);
            if (runeGrade == null)
                return Packet_Result.Result.PacketError;

            CGradeTotalLevelRecord totalRecord = new CGradeTotalLevelRecord();
            CGradeTotalLevelRecord nextTotalRecord = new CGradeTotalLevelRecord();
            if (runeGrade.m_Level < 1)
            {
                totalRecord = CGradeTotalLevelTable.Instance.Find(group_id, 1);
                nextTotalRecord = totalRecord;
            }
            else
            {
                totalRecord = CGradeTotalLevelTable.Instance.Find(group_id, runeGrade.m_Level);
                nextTotalRecord = CGradeTotalLevelTable.Instance.Find(group_id, runeGrade.m_Level + 1);
            }

            if (totalRecord == null)
                return Packet_Result.Result.PacketError;

            if (nextTotalRecord == null)
                return Packet_Result.Result.PacketError;

            CGradeTotalLevelRecord endTotalRecord = CGradeTotalLevelTable.Instance.EndGradeTotalLevelRecord(group_id);
            if (endTotalRecord == null)
                return Packet_Result.Result.PacketError;

            if (endTotalRecord.m_Level <= runeGrade.m_Level)
                return Packet_Result.Result.PacketError;

            CConditionRecord condRecord = CConditionTable.Instance.Find(nextTotalRecord.m_ConditionID);
            if (condRecord == null)
                return Packet_Result.Result.PacketError;

            //condition
            if (false == condRecord.ConditionCheck(m_Owner))
                return Packet_Result.Result.PacketError;

            //process
            runeGrade.m_Level = nextTotalRecord.m_Level;

            m_Owner.StatusAgent.Refresh(new List<eStatusCategory> { eStatusCategory.RuneGrade });

            //db
            CDBManager.Instance.QueryRuneUpdate(m_Owner.DBGUID, m_Owner.SessionKey, m_Owner.AccountID, new CRewardDBMerge(), new CRewardInfo(), runeGrade);

            //log
            var log = LogHelper.PrepareLog(eLog.rune_grade, m_Owner, null, 1);
            log.SetTargetObj(runeGrade.m_GroupID.ToString(), -1, "");
            log.SetSubStr(SJson.ObjectToJson(runeGrade));
            CNetManager.Instance.P2L_ReportLogging(log);

            return Packet_Result.Result.Success;
        }

        public Packet_Result.Result ReqItemRuneCostUnlock(int table_id)
        {
            //valid
            var costData = m_Rune.FindRuneCost(table_id);
            if (costData == null)
                return Packet_Result.Result.PacketError;

            if (costData.m_IsRewarded == true)
                return Packet_Result.Result.PacketError;

            var costRecord = CRuneCostTable.Instance.Find(table_id);
            if (costRecord == null)
                return Packet_Result.Result.DataError;

            var condRecord = CConditionTable.Instance.Find(costRecord.m_ConditionID);
            if (condRecord == null)
                return Packet_Result.Result.DataError;

            //condition
            if (false == condRecord.ConditionCheck(m_Owner))
                return Packet_Result.Result.PacketError;

            //process
            costData.m_IsRewarded = true;

            RefreshRuneCost();

            //db
            CDBManager.Instance.QueryRuneCostUpdate(m_Owner.DBGUID, m_Owner.SessionKey, m_Owner.AccountID, new CRewardDBMerge(), new CRewardInfo(), costData);

            //log
            var log = LogHelper.PrepareLog(eLog.rune_cost, m_Owner, null, 1);
            log.SetTargetObj(costRecord.m_TableID.ToString(), -1, "");
            CNetManager.Instance.P2L_ReportLogging(log);

            return Packet_Result.Result.Success;
        }

        public void AfterQueryRuneGrade(CRewardDBMerge dbtran, _RuneGradeData grade_data, Packet_Result.Result result)
        {
            dbtran.AfterQuery(m_Owner);
            CNetManager.Instance.P2C_ResultItemRuneGradeUnlock(m_Owner.SessionKey, grade_data, result);
        }

        public void AfterQueryRuneCost(CRewardDBMerge dbtran, _RuneCostData cost_Data, Packet_Result.Result result)
        {
            dbtran.AfterQuery(m_Owner);
            CNetManager.Instance.P2C_ResultItemRuneCostUnlock(m_Owner.SessionKey, cost_Data, m_Rune.m_TotalCost, result);
        }

        //cheat
        public void cheat_RuneCostClear()
        {
            foreach (var iter in m_Rune.m_Costlist)
            {
                var itval = iter.Value;
                if (itval.m_IsRewarded == false)
                    continue;

                itval.m_IsRewarded = false;
                //db
                CDBManager.Instance.QueryRuneCostUpdate(m_Owner.DBGUID, m_Owner.SessionKey, m_Owner.AccountID, new CRewardDBMerge(), new CRewardInfo(), itval);
            }

            RefreshRuneCost();

            CNetManager.Instance.P2C_ReportRuneData(m_Owner.SessionKey, GetRuneData());
            CNetManager.Instance.P2C_ResultCheat(m_Owner.SessionKey, Packet_Result.Result.Success);
        }

        //cheat
        public void cheat_RuneGradeClear()
        {
            foreach (var iter in m_Rune.m_Gradelist)
            {
                var itval = iter.Value;
                if (itval.m_Level == 0)
                    continue;

                itval.m_Level = 0;
                m_Owner.StatusAgent.Refresh(new List<eStatusCategory> { eStatusCategory.RuneGrade });

                //db
                CDBManager.Instance.QueryRuneUpdate(m_Owner.DBGUID, m_Owner.SessionKey, m_Owner.AccountID, new CRewardDBMerge(), new CRewardInfo(), itval);
            }

            CNetManager.Instance.P2C_ReportRuneData(m_Owner.SessionKey, GetRuneData());
            CNetManager.Instance.P2C_ResultCheat(m_Owner.SessionKey, Packet_Result.Result.Success);
        }
    }
}

using System;
using System.Threading;
using System.Collections.Generic;
using Steamworks;
using Newtonsoft.Json;

namespace leaderboard
{
    class Program
    {
        static void Main(string[] args)
        {
            try{
                if(!SteamAPI.Init()){
                    Console.WriteLine("SteamAPI Failed to initialize.");
                    return;
                }
                else{
                    WorkshopIndexer.Index();
                    foreach(WorkshopLevel level in WorkshopIndexer.levels)
                    {
                        level.getLeaderboard();
                        string path = level.fileName + ".json";
                        string json = JsonConvert.SerializeObject(level, Formatting.Indented);
                        System.IO.File.WriteAllText(path,json);
                    }
                    SteamAPI.Shutdown();
                }
            }
            catch (DllNotFoundException e){
                Console.WriteLine(e);
                return;
            }
        }
    }

    public class WorkshopIndexer
    {
        static bool done = false;
        static protected CallResult<SteamUGCQueryCompleted_t> m_QueryCompleted;
        static uint index = 0;
        static AppId_t distance = new AppId_t(233610);

        public static List<WorkshopLevel> levels = new List<WorkshopLevel>();

        public static void Index()
        {
            RequestNextPage();
            while(!done){
                SteamAPI.RunCallbacks();
            }
        }

        private static void RequestNextPage()
        {
            index++;
            UGCQueryHandle_t query = SteamUGC.CreateQueryAllUGCRequest(EUGCQuery.k_EUGCQuery_RankedByVote,
                                                                       EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items_ReadyToUse,
                                                                       distance,
                                                                       distance,
                                                                       index);
            SteamUGC.AddRequiredTag(query,"Sprint");
            m_QueryCompleted = CallResult<SteamUGCQueryCompleted_t>.Create(OnQueryCompleted);
            SteamAPICall_t handle = SteamUGC.SendQueryUGCRequest(query);
            m_QueryCompleted.Set(handle);
        }

        private static void IndexPage(SteamUGCQueryCompleted_t param)
        {
            Console.WriteLine("Indexing page " + index);
            if(param.m_unNumResultsReturned == 0)
            {
                done = true;
            }
            else
            {
                for(uint i=0; i < param.m_unNumResultsReturned; i++)
                {
                    SteamUGCDetails_t details;
                    SteamUGC.GetQueryUGCResult(param.m_handle, i, out details);
                    if(details.m_pchFileName.Length > 0)
                    {
                        levels.Add(new WorkshopLevel(details));
                    }
                }
                RequestNextPage();
            }
        }

        private static void OnQueryCompleted(SteamUGCQueryCompleted_t param, bool bIOFailure)
        {
            if(bIOFailure){
                Console.WriteLine("Querying Workshop Failed.");
                return;
            }
            else{
                IndexPage(param);
            }
        }
    }

    public class LeaderboardFetcher
    {
        bool done = false;
        string name;

        List<LeaderboardEntry> leaderboard;

        protected CallResult<LeaderboardFindResult_t> m_FoundLeaderboard;
        protected CallResult<LeaderboardScoresDownloaded_t> m_DownloadedLeaderboard;

        public LeaderboardFetcher(string name)
        {
            this.name = name;
            this.leaderboard = new List<LeaderboardEntry>();
            m_FoundLeaderboard = CallResult<LeaderboardFindResult_t>.Create(OnLeaderboardFound);
            m_DownloadedLeaderboard = CallResult<LeaderboardScoresDownloaded_t>.Create(OnLeaderboardDownloaded);
        }

        public List<LeaderboardEntry> FetchLeaderboard()
        {
            SteamAPICall_t handle = SteamUserStats.FindLeaderboard(name);
            m_FoundLeaderboard.Set(handle);
            Console.WriteLine("Searching for leaderboard " + this.name + ", holup");
            while(!done){
                SteamAPI.RunCallbacks();
            }
            return this.leaderboard;
        }

        private void OnLeaderboardFound(LeaderboardFindResult_t pCallback, bool bIOFailure)
        {
            if(bIOFailure){
                Console.WriteLine("Something went wrong finding the leaderboard");
            }
            else{
                if(pCallback.m_bLeaderboardFound == 1){
                    Console.WriteLine("Leaderboard Found!");
                    SteamAPICall_t handle = SteamUserStats.DownloadLeaderboardEntries(pCallback.m_hSteamLeaderboard,0,0,int.MaxValue);
                    m_DownloadedLeaderboard.Set(handle);
                    Console.WriteLine("Downloading leaderboard " + this.name + ", one sec");
                }else{
                    Console.WriteLine("No leaderboard found");
                    done = true;
                }
            }
        }

        private void OnLeaderboardDownloaded(LeaderboardScoresDownloaded_t param, bool bIOFailure)
        {
            if(bIOFailure){
                Console.WriteLine("Something broke while downloading the leaderboard");
            }
            else{
                done = true;
                Console.WriteLine("Leaderboard Downloaded!");
                for(int i = 0; i < param.m_cEntryCount; i++){
                    LeaderboardEntry_t leaderboardEntry;
                    int[] details = new int[10];
                    SteamUserStats.GetDownloadedLeaderboardEntry(param.m_hSteamLeaderboardEntries,i,out leaderboardEntry,details,10);
                    this.leaderboard.Add(new LeaderboardEntry(leaderboardEntry));
                }
            }
        }
    }

    public class LeaderboardEntry
    {
        public SteamUser player { get; set; }
        public int time { get; set; }
        public LeaderboardEntry(LeaderboardEntry_t leaderboardEntry)
        {
            this.player = new SteamUser(leaderboardEntry.m_steamIDUser);
            this.time = leaderboardEntry.m_nScore;
        }

        override public string ToString()
        {
            TimeSpan ts = TimeSpan.FromMilliseconds(this.time);
            string time = ts.ToString(@"hh\:mm\:ss\.ff");
            return (player.displayName + ": " + time);
        }
    }

    public class SteamUser
    {
        public string displayName;
        public ulong steamID;
        public SteamUser(ulong steamID)
        {
            this.steamID = steamID;
            this.displayName = SteamFriends.GetFriendPersonaName(new CSteamID(steamID));
        }

        public SteamUser(CSteamID steamID)
        {
            this.steamID = steamID.m_SteamID;
            this.displayName = SteamFriends.GetFriendPersonaName(steamID);
        }
    }

    public class WorkshopLevel
    {
        public SteamUser author { get; set; }
        public string displayName { get; set; }
        public string url { get; set; }
        public string description { get; set; }
        public string fileName { get; set; }
        public List<LeaderboardEntry> leaderboard { get; set; }
        public WorkshopLevel(SteamUGCDetails_t details)
        {
            this.displayName = details.m_rgchTitle;
            this.url = details.m_rgchURL;
            this.description = details.m_rgchDescription;
            this.author = new SteamUser(details.m_ulSteamIDOwner);
            this.fileName = details.m_pchFileName;
            this.fileName = this.fileName.Substring(0,fileName.Length - 6);
        }

        public void getLeaderboard()
        {
            string leaderboardName = fileName + "_1_" + author.steamID.ToString() +"_stable";
            LeaderboardFetcher fetch = new LeaderboardFetcher(leaderboardName);
            this.leaderboard = fetch.FetchLeaderboard();

        }
    }
}

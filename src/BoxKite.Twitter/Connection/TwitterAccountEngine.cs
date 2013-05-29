﻿// (c) 2012-2013 Nick Hodge mailto:hodgenick@gmail.com & Brendan Forster
// License: MS-PL

using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BoxKite.Twitter.Helpers;
using BoxKite.Twitter.Models;

namespace BoxKite.Twitter
{
    public partial class TwitterAccount : BindableBase
    {
        readonly Subject<Tweet> _timeline = new Subject<Tweet>();
        public IObservable<Tweet> TimeLine { get { return _timeline; } }

        readonly Subject<Tweet> _mentions = new Subject<Tweet>();
        public IObservable<Tweet> Mentions { get { return _mentions; } }

        readonly Subject<DirectMessage> _directmessages = new Subject<DirectMessage>();
        public IObservable<DirectMessage> DirectMessages { get { return _directmessages; } }

        private List<long> _tweetsSeen = new List<long>();
        private CancellationTokenSource TwitterCommunication;

        public void AddToHomeTimeLine(Tweet t)
        {
            // only Publish if unique
            if (_tweetsSeen.Contains(t.Id)) return;
            _tweetsSeen.Add(t.Id);
            _timeline.OnNext(t);
        }

        public void Start()
        {
            PublicState = "Starting Up";
            TwitterCommunication = new CancellationTokenSource();
            //
            UserStream = Session.GetUserStream();
            UserStream.Tweets.Subscribe(AddToHomeTimeLine);

            // MAGIC HAPPENS HERE
            // there is no specific "catch" for mentions in the Userstream, but here we can fake it!
            // Using LINQ, we can ask RX to show us incoming tweets that contain the screen name of the current user
            // then push this into the "Mentions" Observable
            
            UserStream.Tweets.Where(t => t.Text.ToLower().Contains(accountDetails.ScreenName.ToLower())).Subscribe(_mentions.OnNext);
            UserStream.Start();

            UserStream.DirectMessages.Subscribe(_directmessages.OnNext);

            // MORE MAGIC HAPPENS HERE
            // The Userstreams only get tweets/direct messages from the point the connection is opened.
            // Historical tweets/direct messages have to be gathered using the traditional paging/cursoring APIs
            // (Request/Response REST).
            // but the higher level client doesnt want to worry about all that complexity.
            // in the BackfillPump, we gather these tweets/direct messages and pump them into the correct Observable
            ProcessBackfillPump();
            PublicState = "OK";
        }

        public void Stop()
        {
            PublicState = "Shutting Down";
            TwitterCommunication.Cancel();
            UserStream.Stop();
            PublicState = "";
        }

        private void ProcessBackfillPump()
        {
            Task.Factory.StartNew(GetHomeTimeLine_Backfill);
            Task.Factory.StartNew(GetDirectMessages_Received_Backfill);
            Task.Factory.StartNew(GetRTOfMe_Backfill);
            Task.Factory.StartNew(GetDirectMessages_Sent_Backfill);
            Task.Factory.StartNew(GetMentions_Backfill);
        }

        private async void GetHomeTimeLine_Backfill()
        {
            int backofftimer = 30;
            int maxbackoff = 450;
            long smallestid = 0;
            long largestid = 0;
            int backfillQuota = 50;
            int pagingSize = 50;

            do
            {
                var hometl = await Session.GetHomeTimeline(count: pagingSize, max_id: smallestid);
                if (hometl.OK)
                {
                    smallestid = long.MaxValue;
                    foreach (var tweet in hometl)
                    {
                        AddToHomeTimeLine(tweet);
                        if (tweet.Id < smallestid) smallestid = tweet.Id;
                        if (tweet.Id > largestid) largestid = tweet.Id;
                        backfillQuota--;
                    }
                }
                else
                {
                    // The Backoff will trigger 7 times before just giving up
                    // once at 30s, 60s, 1m, 2m, 4m, 8m and then 16m
                    // note that the last call into this will be 1m above the 15 "rate limit reset" window 
                    Task.Delay(TimeSpan.FromSeconds(backofftimer));
                    if (backofftimer > maxbackoff)
                        break;
                    backofftimer = backofftimer * 2;
                }
            } while (backfillQuota > 0);
        }

        private async void GetMentions_Backfill()
        {
            int backofftimer = 30;
            int maxbackoff = 450;
            long smallestid = 0;
            long largestid = 0;
            int backfillQuota = 50;
            int pagingSize = 50;

            do
            {
                var mentionsofme = await Session.GetMentions(count: pagingSize, max_id: smallestid);
                if (mentionsofme.OK)
                {
                    smallestid = long.MaxValue;
                    foreach (var tweet in mentionsofme)
                    {
                        _mentions.OnNext(tweet);
                        if (tweet.Id < smallestid) smallestid = tweet.Id;
                        if (tweet.Id > largestid) largestid = tweet.Id;
                        backfillQuota--;
                    }
                }
                else
                {
                    // The Backoff will trigger 7 times before just giving up
                    // once at 30s, 60s, 1m, 2m, 4m, 8m and then 16m
                    // note that the last call into this will be 1m above the 15 "rate limit reset" window 
                    Task.Delay(TimeSpan.FromSeconds(backofftimer));
                    if (backofftimer > maxbackoff)
                        break;
                    backofftimer = backofftimer * 2;
                }
            } while (backfillQuota > 0);
        }

        private async void GetRTOfMe_Backfill()
        {
            int backofftimer = 30;
            int maxbackoff = 450;
            long smallestid = 0;
            long largestid = 0;
            int backfillQuota = 20;
            int pagingSize = 20;

            do
            {
                var rtofme = await Session.GetRetweetsOfMe(count: pagingSize, max_id: smallestid);
                if (rtofme.OK)
                {
                    smallestid = long.MaxValue;
                    foreach (var tweet in rtofme)
                    {
                        _mentions.OnNext(tweet);
                        if (tweet.Id < smallestid) smallestid = tweet.Id;
                        if (tweet.Id > largestid) largestid = tweet.Id;
                        backfillQuota--;
                    }
                }
                else
                {
                    // The Backoff will trigger 7 times before just giving up
                    // once at 30s, 60s, 1m, 2m, 4m, 8m and then 16m
                    // note that the last call into this will be 1m above the 15 "rate limit reset" window 
                    Task.Delay(TimeSpan.FromSeconds(backofftimer));
                    if (backofftimer > maxbackoff)
                        break;
                    backofftimer = backofftimer * 2;
                }
            } while (backfillQuota > 0);
        }

        private async void GetDirectMessages_Received_Backfill()
        {
            int backofftimer = 30;
            int maxbackoff = 450;
            long smallestid = 0;
            long largestid = 0;
            int backfillQuota = 20;
            int pagingSize = 20;

            do
            {
                var dmrecd = await Session.GetDirectMessages(count: pagingSize, max_id: smallestid);
                if (dmrecd.OK)
                {
                    smallestid = long.MaxValue;
                    foreach (var dm in dmrecd)
                    {
                        _directmessages.OnNext(dm);
                        if (dm.Id < smallestid) smallestid = dm.Id;
                        if (dm.Id > largestid) largestid = dm.Id;
                        backfillQuota--;
                    }
                }
                else
                {
                    // The Backoff will trigger 7 times before just giving up
                    // once at 30s, 60s, 1m, 2m, 4m, 8m and then 16m
                    // note that the last call into this will be 1m above the 15 "rate limit reset" window 
                    Task.Delay(TimeSpan.FromSeconds(backofftimer));
                    if (backofftimer > maxbackoff)
                        break;
                    backofftimer = backofftimer * 2;
                }
            } while (backfillQuota > 0);
        }

        private async void GetDirectMessages_Sent_Backfill()
        {
            int backofftimer = 30;
            int maxbackoff = 450;
            long smallestid = 0;
            long largestid = 0;
            int backfillQuota = 20;
            int pagingSize = 20;

            do
            {
                var mysentdms = await Session.GetDirectMessagesSent(count: pagingSize, max_id: smallestid);
                if (mysentdms.OK)
                {
                    smallestid = long.MaxValue;
                    foreach (var dm in mysentdms)
                    {
                        _directmessages.OnNext(dm);
                        if (dm.Id < smallestid) smallestid = dm.Id;
                        if (dm.Id > largestid) largestid = dm.Id;
                        backfillQuota--;
                    }
                }
                else
                {
                    // The Backoff will trigger 7 times before just giving up
                    // once at 30s, 60s, 1m, 2m, 4m, 8m and then 16m
                    // note that the last call into this will be 1m above the 15 "rate limit reset" window 
                    Task.Delay(TimeSpan.FromSeconds(backofftimer));
                    if (backofftimer > maxbackoff)
                        break;
                    backofftimer = backofftimer * 2;
                }
            } while (backfillQuota > 0);
        }

    }
}
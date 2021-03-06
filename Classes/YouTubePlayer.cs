﻿
//
// Ported to C# by Nish Anil, Xamarin (@nishanil on twitter)
// 
// Copyright 2014 Google Inc. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using Foundation;
using UIKit;
using CoreGraphics;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using WebKit;
using ObjCRuntime;
using System.Threading.Tasks;

namespace YouTube
{
    #region Enums

    /// <summary>
    /// These enums represent the state of the current video in the player.
    /// </summary>
    public enum PlayerState
    {
        Unstarted,
        Ended,
        Playing,
        Paused,
        Buffering,
        Cued,
        Unknown
    }

    /// <summary>
    /// These enums represent the resolution of the currently loaded video. 
    /// </summary>
    public enum PlaybackQuality
    {
        Small,
        Medium,
        Large,
        HD720,
        HD1080,
        HighRes,
        Auto,
        Default,
        Unknown
        /** This should never be returned. It is here for future proofing. */
    }

    /// <summary>
    /// These enums represent error codes thrown by the player.
    /// </summary>
    public enum PlayerError
    {
        InvalidParam,
        HTML5Error,
        VideoNotFound,
        // Functionally equivalent error codes 100 and
        // 105 have been collapsed into |kYTPlayerErrorVideoNotFound|.
        NotEmbeddable,
        ErrorUnknown
    }

    #endregion

    /// <summary>
    /// YouTubePlayer is a custom UIView that client developers will use to include YouTube
    /// videos in their iOS applications. Use the methods LoadWithVideoId,LoadWithPlaylistId or their 
    /// variants to set the video or playlist to populate the view with. You tube player view.
    /// </summary>
    public class YouTubePlayer : WKNavigationDelegate
    {
        #region Constants

        const string UnstartedCode = @"-1";
        const string EndedCode = @"0";
        const string PlayingCode = @"1";
        const string PausedCode = @"2";
        const string BufferingCode = @"3";
        const string CuedCode = @"5";
        const string UnknownCode = @"unknown";

        // Constants representing playback quality.
        const string SmallQuality = @"small";
        const string MediumQuality = @"medium";
        const string LargeQuality = @"large";
        const string HD720Quality = @"hd720";
        const string HD1080Quality = @"hd1080";
        const string HighResQuality = @"highres";
        const string AutoQuality = @"auto";
        const string DefaultQuality = @"default";
        const string UnknownQuality = @"unknown";

        // Constants representing YouTube player errors.
        const string InvalidParamErrorCode = @"2";
        const string HTML5ErrorCode = @"5";
        const string VideoNotFoundErrorCode = @"100";
        const string NotEmbeddableErrorCode = @"101";
        const string CannotFindVideoErrorCode = @"105";
        const string SameAsNotEmbeddableErrorCode = @"150";

        // Constants representing player callbacks.
        const string OnReady = @"onReady";
        const string OnStateChange = @"onStateChange";
        const string OnPlaybackQualityChange = @"onPlaybackQualityChange";
        const string OnError = @"onError";
        const string OnPlayTime = @"onPlayTime";
        const string OnYouTubeIframeAPIReady = @"onYouTubeIframeAPIReady";
        const string CallbackOnYouTubeIframeAPIFailedToLoad = @"onYouTubeIframeAPIFailedToLoad";

        const string EmbedUrlRegexPattern = @"^http(s)://(www.)youtube.com/embed/(.*)$";
        const string AdUrlRegexPattern = @"^http(s)://pubads.g.doubleclick.net/pagead/conversion/";
        const string OAuthRegexPattern = @"^http(s)://accounts.google.com/o/oauth2/(.*)$";
        const string StaticProxyRegexPattern = @"^https://content.googleapis.com/static/proxy.html(.*)$";
        const string SyndicationRegexPattern = @"^https://tpc.googlesyndication.com/sodar/(.*).html$";

        const string HTMLTemplatePath = @"Assets/YTPlayerView-iframe-player.html";
        const string JSONDataTemplate = "%%jsontemp%%";

        #endregion
        public CGRect Frame { get; set; }

        private NSUrl _OriginURL;
        public NSUrl OriginURL
        {
            get
            {
                if (_OriginURL == null)
                {
                    var bundleId = NSBundle.MainBundle.BundleIdentifier;
                    var stringUrl = $"http://{bundleId.ToLower()}";
                    _OriginURL = NSUrl.FromString(stringUrl);
                }
                return _OriginURL;
            }
            set
            {
                _OriginURL = value;
            }
        }
        public UIView InitialLoadingView { get; set; }

        /// <summary>
        /// Loads the Player with the specified video.
        /// </summary>
        /// <param name="videoId">Video identifier.</param>
        /// <param name="playerVars">Player variables.</param>
        public void LoadWithVideoId(string videoId, Dictionary<string, object> playerVars = null)
        {

            var playerPrams = new Dictionary<string, object> {
                { "videoId", videoId },
				// Optional Param - if Null, LoadPlayer() is set to handle
				{ "playerVars", playerVars }
            };

            this.LoadPlayer(playerPrams);
        }

        /// <summary>
        /// Loads the Player with the playlist.
        /// </summary>
        /// <param name="playlistId">Playlist Id.</param>
        /// <param name="playerVars">Player variables.</param>
        public void LoadWithPlaylistId(string playlistId, Dictionary<string, object> playerVars = null)
        {

            if (playerVars == null)
                playerVars = new Dictionary<string, object>();

            playerVars.Add("listType", "playlist");
            playerVars.Add("list", playlistId);
            this.LoadPlayer(new Dictionary<string, object>() { { "playerVars", playerVars } });
        }

        #region Player Methods

        /// <summary>
        /// Plays the video.
        /// </summary>
        public async Task PlayVideo()
        {
            await EvaluateJavascript("player.playVideo();");
        }

        /// <summary>
        /// Stops the video.
        /// </summary>
        public async Task StopVideo()
        {
           await EvaluateJavascript("player.stopVideo();");
        }

        /// <summary>
        /// Pauses the video.
        /// </summary>
        public async Task PauseVideo()
        {
            //TODO: Why? Obj-C code has it
            NotifyDelegateOfYouTubeCallbackUrl(new NSUrl(string.Format("ytplayer://onStateChange?data=", PausedCode)));
            await EvaluateJavascript("player.pauseVideo();");
        }

        /// <summary>
        /// Seeks to seconds.
        /// </summary>
        /// <param name="seekToSeconds">Seek to seconds.</param>
        /// <param name="allowSeekAhead">If set to <c>true</c> allow seek ahead.</param>
        public async Task SeekTo(double seekToSeconds, bool allowSeekAhead)
        {
           await EvaluateJavascript(string.Format("player.seekTo({0}, {1});", seekToSeconds, allowSeekAhead ? "true" : "false"));
        }

        /// <summary>
        /// Clears the video.
        /// </summary>
        public void ClearVideo()
        {
            EvaluateJavascript("player.clearVideo();");
        }

        #endregion

        #region Cueing Methods

        /// <summary>
        /// Cues the specified video in the player.
        /// </summary>
        /// <param name="videoId">Video identifier.</param>
        /// <param name="startSeconds">Start seconds.</param>
        public void CueVideoById(string videoId, double startSeconds)
        {
            EvaluateJavascript(string.Format("player.cueVideoById('{0}', {1});", videoId, startSeconds));
        }

        /// <summary>
        /// Cues the specified video in the player.
        /// </summary>
        /// <param name="videoId">Video identifier.</param>
        /// <param name="startSeconds">Start seconds.</param>
        /// <param name="endSeconds">End seconds.</param>
        public void CueVideoById(string videoId, double startSeconds, double endSeconds)
        {
            var command = string.Format("{{'videoId': '{0}', 'startSeconds': {1}, 'endSeconds': {2}}}",
                              videoId, startSeconds, endSeconds);
            EvaluateJavascript(string.Format("player.cueVideoById({0});", command));
        }

        /// <summary>
        /// Loads the video.
        /// </summary>
        /// <param name="videoId">Video identifier.</param>
        /// <param name="startSeconds">Start seconds.</param>
        public void LoadVideoById(string videoId, double startSeconds)
        {
            EvaluateJavascript(string.Format("player.loadVideoById('{0}', {1});", videoId, startSeconds));
        }

        /// <summary>
        /// Loads the video.
        /// </summary>
        /// <param name="videoId">Video identifier.</param>
        /// <param name="startSeconds">Start seconds.</param>
        /// <param name="endSeconds">End seconds.</param>
        public void LoadVideoById(string videoId, double startSeconds, double endSeconds)
        {
            var command = string.Format("{{'videoId': '{0}', 'startSeconds': {1}, 'endSeconds': {2}}}",
                              videoId, startSeconds, endSeconds);
            EvaluateJavascript(string.Format("player.loadVideoById({0});", command));
        }

        /// <summary>
        /// Cues video by URL.
        /// </summary>
        /// <param name="videoUrl">URL.</param>
        /// <param name="startSeconds">Start seconds.</param>
        /// <param name="suggestedQuality">Suggested quality.</param>
        public void CueVideoByUrl(string videoUrl, double startSeconds)
        {
            EvaluateJavascript(string.Format("player.cueVideoByUrl('{0}', {1});", videoUrl, startSeconds));
        }

        /// <summary>
        /// Cues video by URL.
        /// </summary>
        /// <param name="videoUrl">Video URL.</param>
        /// <param name="startSeconds">Start seconds.</param>
        /// <param name="endSeconds">End seconds.</param>
        public void CueVideoByUrl(string videoUrl, double startSeconds, double endSeconds)
        {
            var command = string.Format("'{0}', {1}, {2}",
                              videoUrl, startSeconds, endSeconds);
            EvaluateJavascript(string.Format("player.cueVideoByUrl({0});", command));
        }

        /// <summary>
        /// Loads video by URL.
        /// </summary>
        /// <param name="videoUrl">Video URL.</param>
        /// <param name="startSeconds">Start seconds.</param>
        public void LoadVideoByUrl(string videoUrl, double startSeconds)
        {
            EvaluateJavascript(string.Format("player.loadVideoByUrl('{0}', {1});", videoUrl, startSeconds));
        }

        /// <summary>
        /// Loads video by URL.
        /// </summary>
        /// <param name="videoUrl">Video URL.</param>
        /// <param name="startSeconds">Start seconds.</param>
        /// <param name="endSeconds">End seconds.</param>
        public void LoadVideoByUrl(string videoUrl, double startSeconds, double endSeconds)
        {
            var command = string.Format("'{0}', {1}, {2}",
                              videoUrl, startSeconds, endSeconds);
            EvaluateJavascript(string.Format("player.loadVideoByUrl({0});", command));
        }

        #endregion

        #region Cueing Methods for lists

        /// <summary>
        /// Cues the playlist by playlist Id.
        /// </summary>
        /// <param name="playlistId">Playlist identifier.</param>
        /// <param name="index">Index.</param>
        /// <param name="startSeconds">Start seconds.</param>
        public void CuePlaylist(string playlistId, int index, double startSeconds)
        {
            this.CuePlaylistFromCueStr(string.Format("'{0}'", playlistId), index, startSeconds);
        }

        /// <summary>
        /// Cues the playlist by List of Video Ids
        /// </summary>
        /// <param name="videoIds">Video identifiers.</param>
        /// <param name="index">Index.</param>
        /// <param name="startSeconds">Start seconds.</param>
        public void CuePlaylist(List<string> videoIds, int index, double startSeconds)
        {
            //TODO: Test YouTubeAPI Expectation
            var videoIdJsonArray = JsonConvert.SerializeObject(videoIds);
            this.CuePlaylistFromCueStr(videoIdJsonArray, index, startSeconds);
        }

        /// <summary>
        /// Loads the playlist with specified Playlist Id
        /// </summary>
        /// <param name="playlistId">Playlist identifier.</param>
        /// <param name="index">Index.</param>
        /// <param name="startSeconds">Start seconds.</param>
        public void LoadPlaylist(string playlistId, int index, double startSeconds)
        {
            this.LoadPlaylistFromCueStr(string.Format("'{0}'", playlistId), index, startSeconds);
        }

        /// <summary>
        /// Loads the playlist with specified Video Ids.
        /// </summary>
        /// <param name="videoIds">Video identifiers.</param>
        /// <param name="index">Index.</param>
        /// <param name="startSeconds">Start seconds.</param>
        public void LoadPlaylist(List<string> videoIds, int index, double startSeconds)
        {
            //TODO: Test YouTubeAPI Expectation
            var videoIdJsonArray = JsonConvert.SerializeObject(videoIds);
            this.LoadPlaylistFromCueStr(videoIdJsonArray, index, startSeconds);
        }

        #endregion

        #region Playback Rates

        /// <summary>
        /// Gets the playback rate.
        /// </summary>
        /// <returns>The playback rate.</returns>
        /// TODO: Add YTFloatCompletionHandler completionHandler
        public async Task<double> GetPlaybackRate()
        {
            var playbackRate = await EvaluateJavascript("player.getPlaybackRate();");
            return double.Parse(playbackRate);
        }

        /// <summary>
        /// Sets the playback rate.
        /// </summary>
        /// <param name="suggestedRate">Suggested rate.</param>
        public void SetPlaybackRate(double suggestedRate)
        {
            EvaluateJavascript(string.Format("player.setPlaybackRate({0});", suggestedRate));
        }

        /// <summary>
        /// Gets the available playback rates.
        /// </summary>
        /// <returns>The available playback rates.</returns>
        /// TODO: Add YTFloatCompletionHandler completionHandler
        async Task<List<double>> GetAvailablePlaybackRates()
        {
            var str = await EvaluateJavascript("player.getAvailablePlaybackRates();");
            return JsonConvert.DeserializeObject<List<double>>(str);
        }

        #endregion

        #region Setting playback behavior for playlists

        /// <summary>
        /// Sets the loop.
        /// </summary>
        /// <param name="loop">If set to <c>true</c> Enables loop.</param>
        public void SetLoop(bool loop)
        {
            EvaluateJavascript(string.Format(@"player.setLoop({0});", loop));
        }

        /// <summary>
        /// Sets the shuffle.
        /// </summary>
        /// <param name="shuffle">If set to <c>true</c> Enables shuffle.</param>
        public void SetShuffle(bool shuffle)
        {
            EvaluateJavascript(string.Format(@"player.setShuffle({0});", shuffle));
        }

        #endregion

        #region Playback status

        /// <summary>
        /// Gets the video loaded fraction.
        /// </summary>
        /// <returns>The video loaded fraction.</returns>
        /// TODO: Add YTFloatCompletionHandler completionHandler
        public async Task<double> GetVideoLoadedFraction()
        {
            var loadedFraction = await EvaluateJavascript("player.getVideoLoadedFraction();");
            return double.Parse(loadedFraction);
        }

        /// <summary>
        /// Gets the current state of the player.
        /// </summary>
        /// <returns>The player state.</returns>
        /// TODO: Add YTFloatCompletionHandler completionHandler
        public async Task<PlayerState> GetPlayerState()
        {
            var playerState = await EvaluateJavascript("player.getPlayerState();");
            return GetPlayerStateFromString(playerState);
        }

        /// <summary>
        /// Gets the current time.
        /// </summary>
        /// <returns>The current time.</returns>
        /// TODO: Add YTFloatCompletionHandler completionHandler
        public async Task<TimeSpan> GetCurrentTime()
        {
            var currentTime = await EvaluateJavascript("player.getCurrentTime();");
            return TimeSpan.FromSeconds(double.Parse(currentTime));
        }

        /// <summary>
        /// Gets the playback quality.
        /// </summary>
        /// <returns>The playback quality.</returns>
        //public PlaybackQuality GetPlaybackQuality()
        //{
        //	var playBackQlty = EvaluateJavascript("player.getPlaybackQuality();");
        //	return GetPlaybackQualityFromString(playBackQlty);
        //}

        /// <summary>
        /// Sets the playback quality.
        /// </summary>
        /// <param name="suggestedQuality">Suggested quality.</param>
        //public void SetPlaybackQuality(PlaybackQuality suggestedQuality)
        //{
        //	var playBackQltyString = GetStringFromPlaybackQuality(suggestedQuality);
        //	EvaluateJavascript(string.Format("player.setPlaybackQuality('{0}');", playBackQltyString));
        //}

        #endregion

        #region Video Information methods

        /// <summary>
        /// Gets the current video duration.
        /// </summary>
        /// <returns>The duration.</returns>
        /// TODO: Add YTFloatCompletionHandler completionHandler
        public async Task<TimeSpan> GetDuration()
        {
            var duration = await EvaluateJavascript("player.getDuration();");
            return TimeSpan.FromSeconds(double.Parse(duration));
        }

        /// <summary>
        /// Gets the current video URL.
        /// </summary>
        /// <returns>The video URL.</returns>
        /// TODO: Add YTFloatCompletionHandler completionHandler
        public async Task<string> GetVideoUrl()
        {
            var videoUrl = await EvaluateJavascript("player.getVideoUrl();");
            return videoUrl;
        }

        /// <summary>
        /// Gets the current video embed code.
        /// </summary>
        /// <returns>The video embed code.</returns>
        /// TODO: Add YTFloatCompletionHandler completionHandler
        public async Task<string> GetVideoEmbedCode()
        {
            var videoEmbedCode = await EvaluateJavascript("player.getVideoEmbedCode();");
            return videoEmbedCode;
        }

        #endregion

        #region Playlist methods

        /// <summary>
        /// Gets VideoIds in the playlist.
        /// </summary>
        /// <returns>VideoIds in the playlist.</returns>
        /// TODO: Add YTFloatCompletionHandler completionHandler
        public async Task<List<string>> GetPlaylist()
        {
            var videoIds = await EvaluateJavascript("player.getPlaylist();");
            return JsonConvert.DeserializeObject<List<string>>(videoIds);
        }

        /// <summary>
        /// Gets current playlist index.
        /// </summary>
        /// <returns>Playlist index.</returns>
        /// TODO: Add YTFloatCompletionHandler completionHandler
        public async Task<int> GetPlaylistIndex()
        {
            var playListIndex = await EvaluateJavascript("player.getPlaylistIndex();");
            return int.Parse(playListIndex);
        }

        #endregion

        #region Playing a Video in Playlist

        /// <summary>
        /// Plays next Video in the Playlist
        /// </summary>
        public void NextVideo()
        {
            EvaluateJavascript("player.nextVideo();");
        }

        /// <summary>
        /// Plays previous video
        /// </summary>
        public void PreviousVideo()
        {
            EvaluateJavascript("player.previousVideo();");
        }

        /// <summary>
        /// Plays Video at the specified index
        /// </summary>
        /// <param name="index">Index.</param>
        public void PlayVideoAt(int index)
        {
            var command = string.Format("player.playVideoAt({0});", index);
            EvaluateJavascript(command);
        }

        #endregion

        #region Helper methods
        /// <summary>
        /// Convert a quality value from string to the typed enum value.
        /// </summary>
        /// <returns>An enum value representing the playback quality.</returns>
        /// <param name="qualityString">A string representing playback quality. Ex: "small", "medium", "hd1080".</param>
        private PlaybackQuality GetPlaybackQualityFromString(string qualityString)
        {
            var quality = PlaybackQuality.Unknown;

            if (qualityString == SmallQuality)
            {
                quality = PlaybackQuality.Small;
            }
            else if (qualityString == MediumQuality)
            {
                quality = PlaybackQuality.Medium;
            }
            else if (qualityString == LargeQuality)
            {
                quality = PlaybackQuality.Large;
            }
            else if (qualityString == HD720Quality)
            {
                quality = PlaybackQuality.HD720;
            }
            else if (qualityString == HD1080Quality)
            {
                quality = PlaybackQuality.HD1080;
            }
            else if (qualityString == HighResQuality)
            {
                quality = PlaybackQuality.HighRes;
            }

            return quality;
        }

        /// <summary>
        /// Convert a PlaybackQuality value from the typed value to string.
        /// </summary>
        /// <returns>A string value to be used in the JavaScript bridge.</returns>
        /// <param name="quality">PlaybackQuality Parameter.</param>
        private string GetStringFromPlaybackQuality(PlaybackQuality quality)
        {
            switch (quality)
            {
                case PlaybackQuality.Small:
                    return SmallQuality;
                case PlaybackQuality.Medium:
                    return MediumQuality;
                case PlaybackQuality.Large:
                    return LargeQuality;
                case PlaybackQuality.HD720:
                    return HD720Quality;
                case PlaybackQuality.HD1080:
                    return HD1080Quality;
                case PlaybackQuality.HighRes:
                    return HighResQuality;
                case PlaybackQuality.Auto:
                    return AutoQuality;
                default:
                    return UnknownQuality;
            }
        }

        /// <summary>
        /// Convert a state value from NSString to the typed enum value.
        /// </summary>
        /// <returns>An enum value representing the player state.</returns>
        /// <param name="stateString">A string representing player state. Ex: "-1", "0", "1".</param>
        private PlayerState GetPlayerStateFromString(string stateString)
        {
            var state = PlayerState.Unknown;
            if (stateString == UnstartedCode)
            {
                state = PlayerState.Unstarted;
            }
            else if (stateString == EndedCode)
            {
                state = PlayerState.Ended;
            }
            else if (stateString == PlayingCode)
            {
                state = PlayerState.Playing;
            }
            else if (stateString == PausedCode)
            {
                state = PlayerState.Paused;
            }
            else if (stateString == BufferingCode)
            {
                state = PlayerState.Buffering;
            }
            else if (stateString == CuedCode)
            {
                state = PlayerState.Cued;
            }
            return state;

        }

        /// <summary>
        ///  Convert a state value from the typed value to string.
        /// </summary>
        /// <returns>A string value to be used in the JavaScript bridge.</returns>
        /// <param name="state">PlayerState parameter.</param>
        private string GetStringFromPlayerState(PlayerState state)
        {
            switch (state)
            {
                case PlayerState.Unstarted:
                    return UnstartedCode;
                case PlayerState.Ended:
                    return EndedCode;
                case PlayerState.Playing:
                    return PlayingCode;
                case PlayerState.Paused:
                    return PausedCode;
                case PlayerState.Buffering:
                    return BufferingCode;
                case PlayerState.Cued:
                    return CuedCode;
                default:
                    return UnknownCode;
            }
        }

        /// <summary>
        /// Converts a string to PlayerError enum
        /// </summary>
        /// <returns>The player error from string.</returns>
        /// <param name="errorString">Error string.</param>
        private PlayerError GetPlayerErrorFromString(string errorString)
        {
            var error = PlayerError.ErrorUnknown;

            if (errorString == InvalidParamErrorCode)
            {
                error = PlayerError.InvalidParam;
            }
            else if (errorString == HTML5ErrorCode)
            {
                error = PlayerError.HTML5Error;
            }
            else if (errorString == NotEmbeddableErrorCode)
            {
                error = PlayerError.NotEmbeddable;
            }
            else if ((errorString == VideoNotFoundErrorCode) ||
                     (errorString == CannotFindVideoErrorCode))
            {
                error = PlayerError.VideoNotFound;
            }

            return error;
        }
        #endregion

        #region WKNavigationDelegate

        #endregion

        public WKWebView PlayerView
        {
            get;
            set;
        }

        public override void DecidePolicy(WKWebView webView, WKNavigationAction navigationAction, Action<WKNavigationActionPolicy> decisionHandler)
        {
            var request = navigationAction.Request;
            if (request.Url.Scheme.Contains("ytplayer"))
            {
                this.NotifyDelegateOfYouTubeCallbackUrl(request.Url);
                decisionHandler(WKNavigationActionPolicy.Cancel);
                return;
            }
            else if (request.Url.Scheme.Contains("http") || request.Url.Scheme.Contains("https"))
            {
                if (HandleHttpNavigationToUrl(request.Url))
                {
                    decisionHandler(WKNavigationActionPolicy.Allow);
                }
                else
                {
                    decisionHandler(WKNavigationActionPolicy.Cancel);
                }
                return;
            }
            decisionHandler(WKNavigationActionPolicy.Allow);
        }

        [Export("webView:didFailNavigation:withError:")]
        public void DidFailNavigation(WKWebView webView, WKNavigation navigation, NSError error)
        {
            if (InitialLoadingView != null)
                InitialLoadingView.RemoveFromSuperview();
        }

        public YouTubePlayer(CGRect frame)
        {
            this.Frame = frame;
            SetupPlayerView();
        }

        #region Public Events

        public EventHandler PlayerViewReady;
        public EventHandler<PlayerState> PlayerStateChanged;
        public EventHandler<PlaybackQuality> PlayerQualityChanged;
        public EventHandler<PlayerError> PlayerReceivedError;
        public EventHandler<float> PlayerDidPlayTime;
        #endregion

        #region Private Methods

        private void SetupPlayerView()
        {
            //createNewWebView
            var webViewConfiguration = new WKWebViewConfiguration();
            webViewConfiguration.AllowsInlineMediaPlayback = true;
            webViewConfiguration.MediaTypesRequiringUserActionForPlayback = WKAudiovisualMediaTypes.None;
            PlayerView = new WKWebView(Frame, webViewConfiguration);
            PlayerView.AutoresizingMask = (UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight);
            PlayerView.ScrollView.ScrollEnabled = false;
            PlayerView.ScrollView.Bounces = false;
            if (PlayerView.BackgroundColor == UIColor.Clear)
            {
                PlayerView.Opaque = false;
            }
        }

        private void RemoveWebView()
        {
            PlayerView.RemoveFromSuperview();
            PlayerView = null;
        }

        /// <summary>
        /// Raises appropriate C# Events from JavaScript Callbacks
        /// </summary>
        /// <param name="url">URL.</param>
        private void NotifyDelegateOfYouTubeCallbackUrl(NSUrl url)
        {

            // We know the query can only be of the format http://ytplayer?data=SOMEVALUE,
            // so we parse out the value.

            var action = url.Host;
            var query = url.Query;

            string[] data = new string[] { };
            if (!string.IsNullOrEmpty(query))
            {
                data = query.Split('=');
            }
            if (action == OnReady && PlayerViewReady != null)
            {
                PlayerViewReady(this, new EventArgs());
            }
            else if (action == OnStateChange && PlayerStateChanged != null)
            {
                PlayerStateChanged(this, this.GetPlayerStateFromString(data[1]));
            }
            else if (action == OnPlaybackQualityChange && PlayerQualityChanged != null)
            {
                PlayerQualityChanged(this, this.GetPlaybackQualityFromString(data[1]));
            }
            else if (action == OnError && PlayerReceivedError != null)
            {
                PlayerReceivedError(this, this.GetPlayerErrorFromString(data[1]));
            }
            else if (action == OnPlayTime && PlayerDidPlayTime != null)
            {
                float time = 0;
                float.TryParse(data[1], out time);
                PlayerDidPlayTime(this, time);
            }
            else if (action == OnYouTubeIframeAPIReady)
            {
                if (InitialLoadingView != null)
                {
                    InitialLoadingView.RemoveFromSuperview();
                }
            }
        }

        private bool HandleHttpNavigationToUrl(NSUrl url)
        {
            // Usually this means the user has clicked on the YouTube logo or an error message in the
            // player. Most URLs should open in the browser. The only http(s) URL that should open in this
            // UIWebView is the URL for the embed, which is of the format:
            //     http(s)://www.youtube.com/embed/[VIDEO ID]?[PARAMETERS]

            Regex regex = new Regex(EmbedUrlRegexPattern, RegexOptions.IgnoreCase);
            Regex adRegex = new Regex(AdUrlRegexPattern, RegexOptions.IgnoreCase);
            var syndicationRegex = new Regex(SyndicationRegexPattern, RegexOptions.IgnoreCase);
            var oauthRegex = new Regex(OAuthRegexPattern, RegexOptions.IgnoreCase);
            var staticProxyRegex = new Regex(StaticProxyRegexPattern, RegexOptions.IgnoreCase);
            if (regex.IsMatch(url.AbsoluteString) ||
                adRegex.IsMatch(url.AbsoluteString) ||
                syndicationRegex.IsMatch(url.AbsoluteString) ||
                oauthRegex.IsMatch(url.AbsoluteString) ||
                staticProxyRegex.IsMatch(url.AbsoluteString))
                return true;
            else
                UIApplication.SharedApplication.OpenUrl(url);
            return false;

        }

        #endregion

        #region Player Public Methods

        /// <summary>
        /// Loads the player.
        /// </summary>
        /// <param name="playerParams">Player parameters.</param>
        public void LoadPlayer(Dictionary<string, object> playerParams)
        {

            Dictionary<string, string> playerCallbacks = new Dictionary<string, string> {
                { "onReady" , "onReady" },
                { "onStateChange" , "onStateChange" },
                { "onPlaybackQualityChange" , "onPlaybackQualityChange" },
                { "onError" , "onPlayerError" }
            };

            if (playerParams == null)
                playerParams = new Dictionary<string, object>();

            var defaultPlayerParams = new Dictionary<string, object>(playerParams);
            defaultPlayerParams.Add("height", "100%");
            defaultPlayerParams.Add("width", "100%");
            defaultPlayerParams.Add("events", playerCallbacks);

            //if playervars is not already set earlier, then set to default
            if (defaultPlayerParams.ContainsKey("playerVars"))
            {
                if (defaultPlayerParams["playerVars"] == null)
                    defaultPlayerParams["playerVars"] = new Dictionary<string, object>();
            }
            else
            {
                defaultPlayerParams.Add("playerVars", new Dictionary<string, object>());
            }

            string path = Path.Combine(NSBundle.MainBundle.BundlePath, HTMLTemplatePath);

            string embedHTMLTemplate = File.ReadAllText(path);
            if (string.IsNullOrEmpty(embedHTMLTemplate))
                throw new FileNotFoundException(HTMLTemplatePath + " contains invalid text.");

            var jsonData = JsonConvert.SerializeObject(defaultPlayerParams);
            var embedHTML = embedHTMLTemplate.Replace(JSONDataTemplate, jsonData);
            PlayerView.LoadHtmlString(embedHTML, NSUrl.FromString("about:blank"));
            PlayerView.NavigationDelegate = this;
        }

        #region QualityLevels
        /// <summary>
        /// Gets the available quality levels from the player
        /// </summary>
        /// <returns>List of PlaybackQuality</returns>
        //public List<PlaybackQuality> GetAvailableQualityLevels()
        //{

        //    //TODO: This call doesn't seem to be returning anything.
        //    var str = EvaluateJavascript("player.getAvailableQualityLevels();");
        //    var qualityLevelStr = JsonConvert.DeserializeObject<List<string>>(str);
        //    if (qualityLevelStr != null)
        //    {
        //        var qualityLevels = new List<PlaybackQuality>();
        //        foreach (var item in qualityLevelStr)
        //        {
        //            qualityLevels.Add(GetPlaybackQualityFromString(item));
        //        }
        //        return qualityLevels;
        //    }
        //    return null;
        //}
        #endregion

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Cues Playlist Helper Method
        /// </summary>
        /// <param name="cueString">Cue string.</param>
        /// <param name="index">Index.</param>
        /// <param name="startSeconds">Start seconds.</param>
        /// <param name="suggestedQuality">Suggested quality.</param>
        private void CuePlaylistFromCueStr(string cueString, int index, double startSeconds)
        {
            EvaluateJavascript(string.Format("player.cuePlaylist({0}, {1}, {2});", cueString, index, startSeconds));
        }

        /// <summary>
        /// Load Playlist helper method
        /// </summary>
        /// <param name="cueString">Cue string.</param>
        /// <param name="index">Index.</param>
        /// <param name="startSeconds">Start seconds.</param>
        private void LoadPlaylistFromCueStr(string cueString, int index, double startSeconds)
        {
            EvaluateJavascript(string.Format("player.loadPlaylist({0}, {1}, {2});", cueString, index, startSeconds));
        }

        /// <summary>
        /// Runs the JS on WebView
        /// </summary>
        /// <returns>The javascript command.</returns>
        /// <param name="jsToExecute">Js to execute.</param>
        private async Task<string> EvaluateJavascript(string jsToExecute)
        {
            try
            {
                var result = await PlayerView.EvaluateJavaScriptAsync(jsToExecute);
                return result.Description;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        #endregion
    }
}


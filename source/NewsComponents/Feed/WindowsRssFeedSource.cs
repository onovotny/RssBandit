﻿#region Version Info Header
/*
 * $Id$
 * $HeadURL$
 * Last modified by $Author$
 * Last modified at $Date$
 * $Revision$
 */
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.XPath;

using System.Runtime.InteropServices;
using Microsoft.Feeds.Interop;

using RssBandit.Common;

using NewsComponents.Collections;
using NewsComponents.Net;
using NewsComponents.RelationCosmos;
using NewsComponents.Search;
using NewsComponents.Utils;

namespace NewsComponents.Feed {

    /// <summary>
    /// Indicates that an exception occured in the Windows RSS platform and the feed list must be reloaded. 
    /// </summary>
    public class WindowsRssPlatformException : Exception {

        public WindowsRssPlatformException(string message) : base(message) { }
    } 

    /// <summary>
    /// A FeedSource that retrieves user subscriptions and feeds from the Windows RSS platform. 
    /// </summary>
    class WindowsRssFeedSource : FeedSource
    {


        #region constructor

         /// <summary>
        /// Initializes a new instance of the <see cref="FeedSource"/> class.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        public WindowsRssFeedSource(INewsComponentsConfiguration configuration)
        {
            this.p_configuration = configuration;
            if (this.p_configuration == null)
                this.p_configuration = FeedSource.DefaultConfiguration;

            // check for programmers error in configuration:
            ValidateAndThrow(this.Configuration);

            
            IFeedFolder rootFolder = feedManager.RootFolder as IFeedFolder;
            this.AttachEventHandlers(rootFolder);
            feedManager.BackgroundSync(FEEDS_BACKGROUNDSYNC_ACTION.FBSA_ENABLE); 
        }    

        #endregion 


        #region private fields and properties 

        /// <summary>
        /// The Windows RSS platform feed manager
        /// </summary>
        private IFeedsManager feedManager = new FeedsManagerClass();

        /// <summary>
        /// Needed for event handling support with Windows RSS platform
        /// </summary>
        private IFeedFolderEvents_Event fw;

        /// <summary>
        /// Indicates whether an attempt has been made to refresh feeds or not. 
        /// </summary>
        private bool first_refresh_attempt = true; 

        #endregion 

        #region private methods

        /// <summary>
        /// Attaches event handlers to the IFeedFolder
        /// </summary>
        /// <param name="folder"></param>
        private void AttachEventHandlers(IFeedFolder folder)
        {
            fw = (IFeedFolderEvents_Event)folder.GetWatcher(
                    FEEDS_EVENTS_SCOPE.FES_ALL,
                    FEEDS_EVENTS_MASK.FEM_FOLDEREVENTS);
            fw.Error += new IFeedFolderEvents_ErrorEventHandler(Error);
            fw.FeedAdded += new IFeedFolderEvents_FeedAddedEventHandler(FeedAdded);
            fw.FeedDeleted += new IFeedFolderEvents_FeedDeletedEventHandler(FeedDeleted);
            fw.FeedDownloadCompleted += new IFeedFolderEvents_FeedDownloadCompletedEventHandler(FeedDownloadCompleted);
            fw.FeedDownloading += new IFeedFolderEvents_FeedDownloadingEventHandler(FeedDownloading);
            fw.FeedItemCountChanged += new IFeedFolderEvents_FeedItemCountChangedEventHandler(FeedItemCountChanged);
            fw.FeedMovedFrom += new IFeedFolderEvents_FeedMovedFromEventHandler(FeedMovedFrom);
            fw.FeedMovedTo += new IFeedFolderEvents_FeedMovedToEventHandler(FeedMovedTo);
            fw.FeedRenamed += new IFeedFolderEvents_FeedRenamedEventHandler(FeedRenamed);
            fw.FeedUrlChanged += new IFeedFolderEvents_FeedUrlChangedEventHandler(FeedUrlChanged);
            fw.FolderAdded += new IFeedFolderEvents_FolderAddedEventHandler(FolderAdded);
            fw.FolderDeleted += new IFeedFolderEvents_FolderDeletedEventHandler(FolderDeleted);
            fw.FolderItemCountChanged += new IFeedFolderEvents_FolderItemCountChangedEventHandler(FolderItemCountChanged);
            fw.FolderMovedFrom += new IFeedFolderEvents_FolderMovedFromEventHandler(FolderMovedFrom);
            fw.FolderMovedTo += new IFeedFolderEvents_FolderMovedToEventHandler(FolderMovedTo);
            fw.FolderRenamed += new IFeedFolderEvents_FolderRenamedEventHandler(FolderRenamed);                
        }

        /// <summary>
        /// Add a folder to the Windows RSS common feed list
        /// </summary>
        /// <param name="path">The path to the folder</param>
        public IFeedFolder AddFolder(string path)
        {
            IFeedFolder folder = feedManager.RootFolder as IFeedFolder;

            if (!StringHelper.EmptyTrimOrNull(path))
            {
                string[] categoryPath = path.Split(new char[] { '\\' });

                foreach (string c in categoryPath)
                {
                    if (folder.ExistsSubfolder(c))
                    {
                        folder = folder.GetSubfolder(c) as IFeedFolder;
                    }
                    else
                    {
                        folder = folder.CreateSubfolder(c) as IFeedFolder;
                        if (!folder.Path.Equals(path) && !categories.ContainsKey(folder.Path)) {
                            this.categories.Add(folder.Path, new WindowsRssNewsFeedCategory(folder));
                        }
                    }
                }
            }// if (!StringHelper.EmptyTrimOrNull(category))           

            this.AttachEventHandlers(folder);
            return folder;
        }


        #endregion 

        #region public methods

         /// <summary>
        /// Resumes pending BITS downloads from a if any exist. 
        /// </summary>
        public override void ResumePendingDownloads()
        {
            /* Do nothing here. This is handled by the Windows RSS platform automatically */ 
        }


        /// <summary>
        /// Returns the FeedDetails of a feed.
        /// </summary>
        /// <param name="feedUrl">string feed's Url</param>
        /// <returns>FeedInfo or null, if feed was removed or parameter is invalid</returns>
        /* TODO: Why does this lead to InvalidComException later on? */ 
         public override IFeedDetails GetFeedInfo(string feedUrl)
        {
            INewsFeed f = null;
            feedsTable.TryGetValue(feedUrl, out f);
            return f as IFeedDetails; 
        } 

         /// <summary>
        /// Retrieves items from local cache. 
        /// </summary>
        /// <param name="feedUrl"></param>
        /// <returns>A ArrayList of NewsItem objects</returns>
        public override IList<INewsItem> GetCachedItemsForFeed(string feedUrl)
        {
            return this.GetItemsForFeed(feedUrl, false); 
        }

        /// <summary>
        /// Marks all items stored in the internal cache of RSS items as read
        /// for a particular feed.
        /// </summary>
        /// <param name="feed">The RSS feed</param>
        public override void MarkAllCachedItemsAsRead(INewsFeed feed)
        {
            WindowsRssNewsFeed f = feed as WindowsRssNewsFeed;

            if (f != null && !string.IsNullOrEmpty(f.link))
            {
                foreach (INewsItem ri in f.ItemsList)
                {
                    ri.BeenRead = true;
                }
            }
            
        }

        /// <summary>
        /// Retrieves the RSS feed for a particular subscription then converts 
        /// the blog posts or articles to an arraylist of items. 
        /// </summary>
        /// <param name="feedUrl">The URL of the feed to download</param>
        /// <param name="force_download">Flag indicates whether cached feed items 
        /// can be returned or whether the application must fetch resources from 
        /// the web</param>
        /// <exception cref="ApplicationException">If the RSS feed is not 
        /// version 0.91, 1.0 or 2.0</exception>
        /// <exception cref="XmlException">If an error occured parsing the 
        /// RSS feed</exception>
        /// <exception cref="WebException">If an error occurs while attempting to download from the URL</exception>
        /// <exception cref="UriFormatException">If an error occurs while attempting to format the URL as an Uri</exception>
        /// <returns>An arraylist of News items (i.e. instances of the NewsItem class)</returns>		
        //	[MethodImpl(MethodImplOptions.Synchronized)]
        public override IList<INewsItem> GetItemsForFeed(string feedUrl, bool force_download)
        {          
            //We need a reference to the feed so we can see if a cached object exists
            WindowsRssNewsFeed theFeed = null;
            INewsFeed f = null;
            feedsTable.TryGetValue(feedUrl, out f);                

            if (f == null) // not anymore in feedTable
                return EmptyItemList;
            else
                theFeed = f as WindowsRssNewsFeed;

            try
            {
                if (force_download)
                {
                    theFeed.RefreshFeed();
                }

                return theFeed.ItemsList;
            }
            catch (Exception ex)
            {
                Trace("Error retrieving feed '{0}' from cache: {1}", feedUrl, ex.ToString());
            }

            return EmptyItemList; 
        }

        /// <summary>
        /// Adds a category to the list of feed categories known by this feed handler
        /// </summary>
        /// <param name="cat">The category to add</param>
        public override INewsFeedCategory AddCategory(INewsFeedCategory cat)
        {
            if (cat is WindowsRssNewsFeedCategory)
            {
                if (!categories.ContainsKey(cat.Value))
                {
                    categories.Add(cat.Value, cat);
                }
            }
            else
            {
                if (!categories.ContainsKey(cat.Value))
                {
                    IFeedFolder folder = this.AddFolder(cat.Value);
                    cat = new WindowsRssNewsFeedCategory(folder, cat);
                    this.categories.Add(cat.Value, cat);
                }
                else
                {
                    cat = categories[cat.Value];
                }

            }
            
            return cat;
        }

        /// <summary>
        /// Adds a category to the list of feed categories known by this feed handler
        /// </summary>
        /// <param name="cat">The name of the category</param>
        public override INewsFeedCategory AddCategory(string cat)
        {
            INewsFeedCategory toReturn; 

            if (!this.categories.ContainsKey(cat))
            {                
                    IFeedFolder folder = this.AddFolder(cat);
                    toReturn = new WindowsRssNewsFeedCategory(folder);
                    this.categories.Add(cat, toReturn);
            }else{ 
                toReturn = categories[cat];
            }

            return toReturn;
        }


        /// <summary>
        /// Changes the category of a particular INewsFeed. This method should be used instead of setting
        /// the category property of the INewsFeed instance. 
        /// </summary>
        /// <param name="feed">The newsfeed whose category to change</param>
        /// <param name="cat">The new category for the feed. If this value is null then the feed is no longer 
        /// categorized</param>
        public override void ChangeCategory(INewsFeed feed, INewsFeedCategory cat)
        {
            if (feed == null)
                throw new ArgumentNullException("feed");

            if (cat == null)
                throw new ArgumentNullException("cat");

            WindowsRssNewsFeed f = feed as WindowsRssNewsFeed; 

            if (f != null && feedsTable.ContainsKey(f.link))
            {
                IFeedFolder folder = String.IsNullOrEmpty(f.category) ? feedManager.RootFolder as IFeedFolder 
                                                                      : feedManager.GetFolder(f.category) as IFeedFolder;
                IFeed ifeed        = folder.GetFeed(f.title) as IFeed;
                ifeed.Move(cat.Value);                                 
            }
        }

        /// <summary>
        /// Renames the specified category
        /// </summary>        
        /// <param name="oldName">The old name of the category</param>
        /// <param name="newName">The new name of the category</param>        
        public override void RenameCategory(string oldName, string newName)
        {
            if (StringHelper.EmptyTrimOrNull(oldName))
                throw new ArgumentNullException("oldName");

            if (StringHelper.EmptyTrimOrNull(newName))
                throw new ArgumentNullException("newName");

            if (this.categories.ContainsKey(oldName))
            {
                WindowsRssNewsFeedCategory cat = this.categories[oldName] as WindowsRssNewsFeedCategory;
                

                IFeedFolder folder = feedManager.GetFolder(oldName) as IFeedFolder;
                if (folder != null)
                {
                    folder.Rename(newName);
                    this.categories.Remove(oldName);
                    categories.Add(newName, new WindowsRssNewsFeedCategory(folder, cat));
                }
            }
        }
      

        /// <summary>
        /// Adds a feed and associated FeedInfo object to the FeedsTable and itemsTable. 
        /// Any existing feed objects are replaced by the new objects. 
        /// </summary>
        /// <param name="f">The NewsFeed object </param>
        /// <param name="fi">The FeedInfo object</param>
        public override INewsFeed AddFeed(INewsFeed f, FeedInfo fi)
        {
            if (f is WindowsRssNewsFeed)
            {
                if (!feedsTable.ContainsKey(f.link))
                {
                    feedsTable.Add(f.link, f);
                }
            }
            else
            {
                if (!feedManager.ExistsFolder(f.category))
                {
                    this.AddCategory(f.category);
                }

                IFeedFolder folder = feedManager.GetFolder(f.category) as IFeedFolder;
                IFeed newFeed = folder.CreateFeed(f.title, f.link) as IFeed;
                f = new WindowsRssNewsFeed(newFeed, f);
                feedsTable.Add(f.link, f);
            }

            return f;
        }

        /// <summary>
        /// Deletes all subscribed feeds and categories 
        /// </summary>
        public override void DeleteAllFeedsAndCategories()
        {
            string[] keys = new string[categories.Count];
            this.categories.Keys.CopyTo(keys, 0);
            foreach (string categoryName in keys)
            {
                this.DeleteCategory(categoryName);
            }

            keys = new string[feedsTable.Count];
            this.feedsTable.Keys.CopyTo(keys, 0);
            foreach (string feedUrl in keys) {
                this.DeleteFeed(feedUrl);             
            }          

            base.DeleteAllFeedsAndCategories();             
        }

        /// <summary>
        /// Removes all information related to a feed from the FeedSource.   
        /// </summary>
        /// <remarks>If no feed with that URL exists then nothing is done.</remarks>
        /// <param name="feedUrl">The URL of the feed to delete. </param>
        /// <exception cref="ApplicationException">If an error occured while 
        /// attempting to delete the cached feed. Examine the InnerException property 
        /// for details</exception>
        public override void DeleteFeed(string feedUrl)
        {
            if (feedsTable.ContainsKey(feedUrl))
            {
                WindowsRssNewsFeed f = feedsTable[feedUrl] as WindowsRssNewsFeed;
                this.feedsTable.Remove(f.link);                
                IFeed feed = feedManager.GetFeedByUrl(feedUrl) as IFeed;
                
                if (feed != null)
                {
                    feed.Delete();
                }
            }
        }

        /// <summary>
        /// Deletes a category from the Categories collection. 
        /// </summary>
        /// <remarks>This also deletes the corresponding folder from the underlying Windows RSS platform.</remarks>
        /// <param name="cat"></param>
        public override void DeleteCategory(string cat)
        {
            base.DeleteCategory(cat);

            IFeedFolder folder = feedManager.GetFolder(cat) as IFeedFolder;
            if (folder != null)
            {
                folder.Delete();
            }
        }

      


        /// <summary>
        /// Used to recursively load a folder and its children (feeds and subfolders) into the FeedsTable and Categories collections. 
        /// </summary>
        /// <param name="folder2load">The folder to load</param>
        /// <param name="bootstrapFeeds">The RSS Bandit metadata about the feeds being loaded</param>
        /// <param name="bootstrapCategories">The RSS Bandit metadata about the folders/categories being loaded</param>
        private void LoadFolder(IFeedFolder folder2load, Dictionary<string, NewsFeed> bootstrapFeeds, Dictionary<string, INewsFeedCategory> bootstrapCategories)
        {

            if (folder2load != null)
            {                
                IFeedsEnum Feeds = folder2load.Feeds as IFeedsEnum;
                IFeedsEnum Subfolders = folder2load.Subfolders as IFeedsEnum;

                if (Feeds.Count > 0)
                {
                    foreach (IFeed feed in Feeds)
                    {
                        Uri uri = null;

                        try
                        {
                            uri = new Uri(feed.DownloadUrl);
                        }
                        catch (Exception)
                        {
                            try
                            {
                                uri = new Uri(feed.Url);
                            }
                            catch (Exception)
                            {
                                continue; 
                            }
                        }
                        string feedUrl = uri.CanonicalizedUri();
                        INewsFeed bootstrapFeed = (bootstrapFeeds.ContainsKey(feedUrl) ? bootstrapFeeds[feedUrl] : null);
                        this.feedsTable.Add(feedUrl, new WindowsRssNewsFeed(feed, bootstrapFeed));                         
                    }//foreach(IFeed feed in ...)
                }

                if (Subfolders.Count > 0)
                {
                    foreach (IFeedFolder folder in Subfolders)
                    {
                        string categoryName = folder.Path;
                        INewsFeedCategory bootstrapCategory = (bootstrapCategories.ContainsKey(categoryName) ? bootstrapCategories[categoryName] : null);
                        this.categories.Add(folder.Path, new WindowsRssNewsFeedCategory(folder, bootstrapCategory));
                        LoadFolder(folder, bootstrapFeeds, bootstrapCategories);  
                    }
                }
            }

        }

        /// <summary>
        /// Loads the feedlist from the FeedLocation. 
        ///</summary>
        public override void LoadFeedlist()
        {
            this.BootstrapAndLoadFeedlist(new feeds());
        }

        /// <summary>
        /// Loads the feedlist from the feedlocation and use the input feedlist to bootstrap the settings. The input feedlist
        /// is also used as a fallback in case the FeedLocation is inaccessible (e.g. we are in offline mode and the feed location
        /// is on the Web). 
        /// </summary>
        /// <param name="feedlist">The feed list to provide the settings for the feeds downloaded by this FeedSource</param>
        public override void BootstrapAndLoadFeedlist(feeds feedlist)
        {
            Dictionary<string, NewsFeed> bootstrapFeeds = new Dictionary<string, NewsFeed>();
            Dictionary<string, INewsFeedCategory> bootstrapCategories = new Dictionary<string, INewsFeedCategory>();

            foreach (NewsFeed f in feedlist.feed) 
            {
                bootstrapFeeds.Add(f.link, f); 
            }

            foreach (category c in feedlist.categories) 
            {
                bootstrapCategories.Add(c.Value, c);   
            }

            IFeedFolder root = feedManager.RootFolder as IFeedFolder;
            LoadFolder(root, bootstrapFeeds, bootstrapCategories);
                         
            //feedManager.BackgroundSync(FEEDS_BACKGROUNDSYNC_ACTION.FBSA_ENABLE); 
        }


        /// <summary>
        /// Retrieves the RSS feed for a particular subscription then converts 
        /// the blog posts or articles to an arraylist of items. The http requests are async calls.
        /// </summary>
        /// <param name="feedUrl">The URL of the feed to download</param>
        /// <param name="force_download">Flag indicates whether cached feed items 
        /// can be returned or whether the application must fetch resources from 
        /// the web</param>
        /// <param name="manual">Flag indicates whether the call was initiated by user (true), or
        /// by automatic refresh timer (false)</param>
        /// <exception cref="ApplicationException">If the RSS feed is not version 0.91, 1.0 or 2.0</exception>
        /// <exception cref="XmlException">If an error occured parsing the RSS feed</exception>
        /// <exception cref="ArgumentNullException">If feedUrl is a null reference</exception>
        /// <exception cref="UriFormatException">If an error occurs while attempting to format the URL as an Uri</exception>
        /// <returns>true, if the request really was queued up</returns>
        /// <remarks>Result arraylist is returned by OnUpdatedFeed event within UpdatedFeedEventArgs</remarks>		
        //	[MethodImpl(MethodImplOptions.Synchronized)]
        public override bool AsyncGetItemsForFeed(string feedUrl, bool force_download, bool manual)
        {
            if (feedUrl == null || feedUrl.Trim().Length == 0)
                throw new ArgumentNullException("feedUrl");

            INewsFeed f = null;
            feedsTable.TryGetValue(feedUrl, out f);
            WindowsRssNewsFeed f2 = f as WindowsRssNewsFeed;

            if (f2 != null)
            {                
                f2.RefreshFeed(); 
            }

            return true;
        }

        /// <summary>
        /// Downloads every feed that has either never been downloaded before or 
        /// whose elapsed time since last download indicates a fresh attempt should be made. 
        /// </summary>
        /// <param name="force_download">A flag that indicates whether download attempts should be made 
        /// or whether the cache can be used.</param>
        /// <remarks>This method uses the cache friendly If-None-Match and If-modified-Since
        /// HTTP headers when downloading feeds.</remarks>	
        public override void RefreshFeeds(bool force_download)
        {
            string[] keys = GetFeedsTableKeys();

            if (force_download)
            {               
                for (int i = 0, len = keys.Length; i < len; i++)
                {
                    if (!feedsTable.ContainsKey(keys[i])) // may have been redirected/removed meanwhile
                        continue;

                    WindowsRssNewsFeed current = feedsTable[keys[i]] as WindowsRssNewsFeed;
                    current.RefreshFeed();
                   
                    //Thread.Sleep(15); // force a context switches
                } //for(i)
                
            }
            else if (first_refresh_attempt) 
            {
                for (int i = 0, len = keys.Length; i < len; i++)
                {
                    if (!feedsTable.ContainsKey(keys[i])) // may have been redirected/removed meanwhile
                        continue;

                    //let the UI know that we have items since LoadItemsList() was called in WindowsRssNewsFeed constructor
                    WindowsRssNewsFeed current = feedsTable[keys[i]] as WindowsRssNewsFeed;
                    this.RaiseOnUpdatedFeed(new Uri(current.link), null, RequestResult.OK, 1110, false); 

                } //for(i)
                this.first_refresh_attempt = false;
            }
        }

        /// <summary>
        /// Downloads every feed that has either never been downloaded before or 
        /// whose elapsed time since last download indicates a fresh attempt should be made. 
        /// </summary>
        /// <param name="category">Refresh all feeds, that are part of the category</param>
        /// <param name="force_download">A flag that indicates whether download attempts should be made 
        /// or whether the cache can be used.</param>
        /// <remarks>This method uses the cache friendly If-None-Match and If-modified-Since
        /// HTTP headers when downloading feeds.</remarks>	
        public override void RefreshFeeds(string category, bool force_download)
        {
            //RaiseOnUpdateFeedsStarted(force_download);
            string[] keys = GetFeedsTableKeys();

            for (int i = 0, len = keys.Length; i < len; i++)
            {
                if (!feedsTable.ContainsKey(keys[i])) // may have been redirected/removed meanwhile
                    continue;

                WindowsRssNewsFeed current = feedsTable[keys[i]] as WindowsRssNewsFeed;

                if (current.category != null && IsChildOrSameCategory(category, current.category))
                {
                    current.RefreshFeed();
                }

                Thread.Sleep(15); // force a context switches
            } //for(i)

        }

        #endregion

        #region IFeedFolderEvents implementation

        /// <summary>
        /// Occurs when a feed event error occurs.
        /// </summary>
        /// <remarks>The advice in documentation for when this happens is that the application must assume that some events have 
        /// not been raised, and should recover by rereading the feed subscription list as if running for the first time. 
        /// </remarks>
        public void Error()
        {
            throw new WindowsRssPlatformException("Windows RSS platform has raised an error. Please reload the Windows RSS feed list"); 
        }

        /// <summary>
        /// A subfolder was added.
        /// </summary>
        /// <param name="Path">The path to the folder</param>
        public void FolderAdded(string Path)
        {
            this.categories.Add(Path, new WindowsRssNewsFeedCategory(feedManager.GetFolder(Path) as IFeedFolder));
            this.readonly_categories = new ReadOnlyDictionary<string, INewsFeedCategory>(this.categories);
            RaiseOnAddedCategory(new CategoryEventArgs(Path));
        }

        /// <summary>
        /// A subfolder was added.
        /// </summary>
        /// <param name="Path">The path to the folder</param>
        public void FolderDeleted(string Path)
        {
            this.categories.Remove(Path);
            this.readonly_categories = new ReadOnlyDictionary<string, INewsFeedCategory>(this.categories);
            RaiseOnDeletedCategory(new CategoryEventArgs(Path));
        }

        /// <summary>
        /// A subfolder was moved from this folder to another folder.
        /// </summary>
        /// <param name="Path"></param>
        /// <param name="oldPath"></param>
        public static void FolderMovedFrom(string Path, string oldPath)
        {
            Console.WriteLine("Folder moved from {0} to {1}", oldPath, Path); 
         
         /* Do nothing since we get the same event repeated in FolderMoveTo */  
        }
        
        /// <summary>
        /// A subfolder was moved into this folder.
        /// </summary>
        /// <param name="Path"></param>
        /// <param name="oldPath"></param>
        public void FolderMovedTo(string Path, string oldPath)
        {
            INewsFeedCategory cat = this.categories[oldPath];
            this.categories.Remove(oldPath);
            this.categories.Add(Path, new WindowsRssNewsFeedCategory(feedManager.GetFolder(Path) as IFeedFolder, cat));
            this.readonly_categories = new ReadOnlyDictionary<string, INewsFeedCategory>(this.categories);

            RaiseOnMovedCategory(new CategoryChangedEventArgs(oldPath, Path));
        }


        /// <summary>
        /// A subfolder was renamed.
        /// </summary>
        /// <param name="Path"></param>
        /// <param name="oldPath"></param>
        public void FolderRenamed(string Path, string oldPath)
        {

            INewsFeedCategory cat = this.categories[oldPath];
            this.categories.Remove(oldPath);
            this.categories.Add(Path, new WindowsRssNewsFeedCategory(feedManager.GetFolder(Path) as IFeedFolder, cat));
            this.readonly_categories = new ReadOnlyDictionary<string, INewsFeedCategory>(this.categories);

            RaiseOnRenamedCategory(new CategoryChangedEventArgs(oldPath, Path));
        }

        /// <summary>
        /// Occurs when the aggregated item count of a feed folder changes.
        /// </summary>
        /// <param name="Path"></param>
        /// <param name="itemCountType"></param>
        public void FolderItemCountChanged(string Path, int itemCountType)
        {
            /* Do nothing since we also get events from FeedItemCountChanged */ 
        }

        /// <summary>
        /// Occurs when a feed is added to the folder.
        /// </summary>
        /// <param name="Path"></param>
        public void FeedAdded(string Path)
        {
            IFeed ifeed = feedManager.GetFeed(Path) as IFeed;
            this.feedsTable.Add(ifeed.DownloadUrl, new WindowsRssNewsFeed(ifeed));
            this.readonly_feedsTable = new ReadOnlyDictionary<string, INewsFeed>(this.feedsTable);

            RaiseOnAddedFeed(new FeedChangedEventArgs(ifeed.DownloadUrl));
        }

        /// <summary>
        /// A feed was deleted.
        /// </summary>
        /// <param name="Path"></param>
        public void FeedDeleted(string Path)
        {
            int index = Path.LastIndexOf(FeedSource.CategorySeparator);
            string categoryName = null, title = null;

            if (index == -1)
            {
                title = Path;
            }
            else
            {
                categoryName = Path.Substring(0, index);
                title = Path.Substring(index + 1);
            }

            string[] keys = GetFeedsTableKeys();

            for (int i = 0; i < keys.Length; i++)
            {
                INewsFeed f = null;
                feedsTable.TryGetValue(keys[i], out f);

                if (f != null)
                {
                    if (f.title.Equals(title) && (Object.Equals(f.category, categoryName)))
                    {
                        this.feedsTable.Remove(f.link);
                        this.readonly_feedsTable = new ReadOnlyDictionary<string, INewsFeed>(this.feedsTable);

                        RaiseOnDeletedFeed(new FeedDeletedEventArgs(f.link, f.title));
                        break;
                    }
                }
            }

        }

        /// <summary>
        /// A feed was renamed.
        /// </summary>
        /// <param name="Path"></param>
        /// <param name="oldPath"></param>
        public void FeedRenamed(string Path, string oldPath)
        {
            int index = oldPath.LastIndexOf(FeedSource.CategorySeparator);
            string categoryName = null, title = null;

            if (index == -1)
            {
                title = oldPath;
            }
            else
            {
                categoryName = oldPath.Substring(0, index);
                title = oldPath.Substring(index + 1);
            }

            string[] keys = GetFeedsTableKeys();

            for (int i = 0; i < keys.Length; i++)
            {
                INewsFeed f = null;
                feedsTable.TryGetValue(keys[i], out f);

                if (f != null)
                {
                    if (f.title.Equals(title) && (Object.Equals(f.category, categoryName)))
                    {
                        index = Path.LastIndexOf(FeedSource.CategorySeparator);
                        string newTitle = (index == -1 ? Path : Path.Substring(index + 1));

                        RaiseOnRenamedFeed(new FeedRenamedEventArgs(f.link, newTitle));
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// A feed was moved from this folder to another folder.
        /// </summary>
        /// <param name="Path"></param>
        /// <param name="oldPath"></param>
        public static void FeedMovedFrom(string Path, string oldPath)
        {
            Console.WriteLine("Feed moved from {0} to {1}", oldPath, Path); 
            /* Do nothing since we get the same event repeated in FeedMoveTo */
        }

        /// <summary>
        /// A feed was moved to this folder.
        /// </summary>
        /// <param name="Path"></param>
        /// <param name="oldPath"></param>
        public void FeedMovedTo(string Path, string oldPath)
        {
            int index = oldPath.LastIndexOf(FeedSource.CategorySeparator);
            string categoryName = null, title = null;

            if (index == -1)
            {
                title = oldPath;
            }
            else
            {
                categoryName = oldPath.Substring(0, index);
                title = oldPath.Substring(index + 1);
            }

            string[] keys = GetFeedsTableKeys();

            for (int i = 0; i < keys.Length; i++)
            {
                INewsFeed f = null;
                feedsTable.TryGetValue(keys[i], out f);

                if (f != null)
                {
                    if (f.title.Equals(title) && (Object.Equals(f.category, categoryName)))
                    {
                        index = Path.LastIndexOf(FeedSource.CategorySeparator);
                        string newCategory = (index == -1 ? Path : Path.Substring(0, index));

                        RaiseOnMovedFeed(new FeedMovedEventArgs(f.link, newCategory));
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// The URL of a feed changed.
        /// </summary>
        /// <param name="Path"></param>
        public void FeedUrlChanged(string Path)
        {
            IFeed ifeed = feedManager.GetFeed(Path) as IFeed;
            int index = Path.LastIndexOf(FeedSource.CategorySeparator);
            string categoryName = null, title = null;

            if (index == -1)
            {
                title = Path;
            }
            else
            {
                categoryName = Path.Substring(0, index);
                title = Path.Substring(index + 1);
            }

            string[] keys = GetFeedsTableKeys();

            for (int i = 0; i < keys.Length; i++)
            {
                INewsFeed f = null;
                feedsTable.TryGetValue(keys[i], out f);

                if (f != null)
                {
                    if (f.title.Equals(title) && (Object.Equals(f.category, categoryName)))
                    {
                        Uri requestUri = new Uri(f.link);
                        Uri newUri = new Uri(ifeed.DownloadUrl);
                        RaiseOnUpdatedFeed(requestUri, newUri, RequestResult.NotModified, 1110, false);
                        break;
                    }
                }
            }//for

        }


        /// <summary>
        /// The number of items or unread items in a feed changed.
        /// </summary>
        /// <param name="Path"></param>
        /// <param name="itemCountType"></param>
        public void FeedItemCountChanged(string Path, int itemCountType)
        {

            IFeed ifeed = feedManager.GetFeed(Path) as IFeed;
            Uri requestUri = new Uri(ifeed.DownloadUrl);
            RaiseOnUpdatedFeed(requestUri, null, RequestResult.OK, 1110, false);
        }


        /// <summary>
        /// A feed has started downloading.
        /// </summary>
        /// <param name="Path"></param>
        public void FeedDownloading(string Path)
        {

            IFeed ifeed = feedManager.GetFeed(Path) as IFeed;
            Uri requestUri = new Uri(ifeed.DownloadUrl);
            RaiseOnUpdateFeedStarted(requestUri, true, 1110);
        }       
         

        /// <summary>
        /// A feed has completed downloading (success or error).
        /// </summary>
        /// <param name="Path"></param>
        /// <param name="Error"></param>
        public void FeedDownloadCompleted(string Path, FEEDS_DOWNLOAD_ERROR Error)
        {
            IFeed ifeed = feedManager.GetFeed(Path) as IFeed;
            Uri requestUri = new Uri(ifeed.DownloadUrl);

            if (Error == FEEDS_DOWNLOAD_ERROR.FDE_NONE)
            {
                RaiseOnUpdatedFeed(requestUri, null, RequestResult.OK, 1110, false);
            }
            else
            {
                INewsFeed f = null;
                feedsTable.TryGetValue(ifeed.DownloadUrl, out f);
                WindowsRssNewsFeed wf = f as WindowsRssNewsFeed;

                if (wf == null)
                {
                    Exception e = new FeedRequestException(Error.ToString(), new WebException(Error.ToString()), FeedSource.GetFailureContext(wf, wf));
                    RaiseOnUpdateFeedException(ifeed.DownloadUrl, e, 1100);
                }

            }
        }

        #endregion

    }

    #region WindowsRssNewsItem 


    /// <summary>
    /// Represents a NewsItem obtained from the Windows RSS platform
    /// </summary>
    public class WindowsRssNewsItem : INewsItem, IDisposable
    {

        #region constructors 

        /// <summary>
        /// We always want an associated IFeedItem instance
        /// </summary>
        private WindowsRssNewsItem() { ;}

          /// <summary>
        /// Initializes the class
        /// </summary>
        /// <param name="item">The IFeedItem instance that this object will wrap</param>
        public WindowsRssNewsItem(IFeedItem item, WindowsRssNewsFeed owner)
        {
            if (item == null) throw new ArgumentNullException("item"); 
            this.myitem = item;
            this.myfeed = owner;
            /* do this here because COM interop is too slow to check it each time property is accessed */
            this._id = myitem.LocalId.ToString();            

            //TODO: RelationCosmos and outgoing links processing? 
        }


        #endregion 

        #region private fields

        /// <summary>
        /// Indicates that the object has been disposed
        /// </summary>
        private bool disposed = false;

        /// <summary>
        /// The actual IFeedItem instance that this object is wrapping
        /// </summary>
        private IFeedItem myitem = null;

        /// <summary>
        /// The INewsFeed instance which this item belongs to
        /// </summary>
        private WindowsRssNewsFeed myfeed = null;

        #endregion 

          #region destructor and IDisposable implementation 

        /// <summary>
        /// Releases the associated COM objects
        /// </summary>
        /// <seealso cref="myitem"/>
        ~WindowsRssNewsItem() {
            Dispose(false);           
        }

        /// <summary>
        /// Disposes of the class
        /// </summary>
        public void Dispose()
        {

            if (!disposed)
            {
                Dispose(true);
            }

        }

        /// <summary>
        /// Disposes of the class
        /// </summary>
        /// <param name="disposing"></param>
        public void Dispose(bool disposing)
        {
            lock (this)
            {                
                if (myitem != null)
                {
                    Marshal.ReleaseComObject(myitem);
                }
                System.GC.SuppressFinalize(this);
                disposed = true; 
            }
        }

        #endregion


        #region INewsItem implementation 

        /// <summary>
        /// Container of enclosures on the item. If there are no enclosures on the item
        /// then this value is null. 
        /// </summary>
        /// <remarks>This property is read only</remarks>
        public List<IEnclosure> Enclosures
        {
            get
            {
                if (myitem.Enclosure != null)
                {
                    IEnclosure enc = new WindowsRssEnclosure(myitem.Enclosure as IFeedEnclosure);
                    return new List<IEnclosure>() { enc };
                }
                else
                {
                    return GetList<IEnclosure>.Empty;
                }
            }
            set
            {
                /* Can't set IFeedItem.Enclosure */ 
            }
        }

        private bool p_hasNewComments;
        /// <summary>
        /// Indicates that there are new comments to this item. 
        /// </summary>
        public bool HasNewComments
        {
            get
            {
                return p_hasNewComments;
            }
            set
            {
                p_hasNewComments = value;
            }
        }

        private bool p_watchComments;
        /// <summary>
        /// Indicates that comments to this item are being watched. 
        /// </summary>
        public bool WatchComments
        {
            get
            {
                return p_watchComments;
            }
            set
            {
                p_watchComments = value;
            }
        }

        private Flagged p_flagStatus = Flagged.None;
        /// <summary>
        /// Indicates whether the item has been flagged for follow up or not. 
        /// </summary>
        public Flagged FlagStatus
        {
            get
            {
                return p_flagStatus;
            }
            set
            {
                p_flagStatus = value;
            }
        }

        /// <summary>
        /// Indicates whether this item supports posting comments. 
        /// </summary>
        /// <remarks>This property is read only</remarks>
        public SupportedCommentStyle CommentStyle
        {
            get
            {
                return SupportedCommentStyle.None;
            }
            set
            {
                /* do nothing */ 
            }
        }

         /// <summary>
        /// Gets or sets the language of the entry.
        /// Format of the corresponfing attribute as defined in
        /// http://www.w3.org/TR/REC-xml/#sec-lang-tag;
        /// Format of the language string: 
        /// see http://www.ietf.org/rfc/rfc3066.txt
        /// </summary>
        /// <value>The language.</value>
        public string Language
        {
            get { return myfeed.Language; }
        }

        /// <summary>
        /// Gets the feed link (source the feed is requested from) the item belongs to.
        /// </summary>
        public string FeedLink
        {
            get { return (myitem.Parent as IFeed).DownloadUrl; }
        }

        /// <summary>
        /// The link to the item.
        /// </summary>
        public string Link
        {
            get
            {
                try
                {
                    return myitem.Link;
                }
                catch
                {
                    return String.Empty;
                }
            }
        }

        /// <summary>
        /// The date the article or blog entry was made. 
        /// </summary>
        /// <remarks>This field is read only </remarks>
        public DateTime Date
        {
            get
            {
                try
                {
                    return myitem.PubDate;
                }
                catch (Exception) /* thrown if Windows RSS platform can't parse the date */
                {
                    return myitem.LastDownloadTime; 
                }
            }
            set { /* can't set IFeedItem.PubDate */ }
        }

        private string _id; 
        /// <summary>
        /// The unique identifier.
        /// </summary>
        /// <remarks>This property is read only</remarks>
        public string Id
        {
            get { return _id; }
            set { /* Can't set IFeedItem.LocalId */ }
        }

        /// <summary>
        /// The unique identifier of the parent.
        /// </summary>
        public string ParentId
        {
            get { return null; }
        }

        /// <summary>
        /// The content of the article or blog entry. 
        /// </summary>
        public string Content
        {
            get { return myitem.Description; }
        }

        /// <summary>
        /// Returns true, if Content contains something; else false.
        /// </summary>
        /// <remarks>Should be used instead of testing 
        /// (Content != null &amp;&amp; Content.Length > 0) and is equivalent to 
        /// .ContentType == ContentType.None
        /// </remarks>
        public bool HasContent
        { 
            get { return true; } 
        }

        /// <summary>
        /// Set new content of the article or blog entry.
        /// </summary>
        /// <remarks>WARNING: This method does nothing.</remarks>
        /// <param name="newContent">string</param>
        /// <param name="contentType">ContentType</param>
        public void SetContent(string newContent, ContentType contentType)
        {
            /* Can't set IFeedItem.Description */ 
        }

        /// <summary>
        /// Indicates whether the description on this feed is text, HTML or XHTML. 
        /// </summary>
        /// <remarks>This property is read only</remarks>
        public ContentType ContentType
        {
            get { return ContentType.Html; }
            set { } 
        }

        /// <summary>
        /// Indicates whether the story has been read or not. 
        /// </summary>
        public bool BeenRead
        {
            get { return myitem.IsRead; }
            set { myitem.IsRead = value;}
        }

        /// <summary>
        /// Returns an object implementing the FeedDetails interface to which this item belongs
        /// </summary>
        public IFeedDetails FeedDetails {
            get { return this.myfeed; }
            set
            {
                if (value is WindowsRssNewsFeed)
                    this.myfeed = value as WindowsRssNewsFeed;
            }
        }

        /// <summary>
        /// The author of the article or blog entry 
        /// </summary>
        /// <remarks>This property is read only</remarks>
        public string Author {
            get { return this.myitem.Author; }
            set { /* Can't set IFeedItem.Author */ }
        }

        /// <summary>
        /// The title of the article or blog entry. 
        /// </summary>
        /// <remarks>This property is read only</remarks>
       public string Title { 
            get { return myitem.Title; }
            set { /* Can't set IFeedItem.Title */ } 
       }

        /// <summary>
        /// The subject of the article or blog entry. 
        /// </summary>
        /// <remarks>This property is read only</remarks>
        public string Subject {
            get { return null; }
            set { /* not supported */ }
        }

        /// <summary>
        /// Returns the amount of comments attached.
        /// </summary>
        /// <remarks>This property is read only</remarks>
        public int CommentCount
        {
            get { return 0; }
            set { /* */ }
        }

        /// <summary>the URL to post comments to</summary>
        public string CommentUrl
        {
            get{ return null;}
        }

        /// <summary>the URL to get an RSS feed of comments from</summary>
        /// <remarks>This is not exposed in the Windows RSS platform data model</remarks>
        public string CommentRssUrl {
            get { return null; }
            set { /* can't be set */ }
        }

        private Hashtable _optionalElements = new Hashtable(); 
        /// <summary>
        /// Container for all the optional RSS elements for an item. Also 
        /// holds information from RSS modules. The keys in the hashtable 
        /// are instances of XmlQualifiedName while the values are instances 
        /// of XmlNode. 
        /// </summary>
        /// <remarks>Setting this field may have the side effect of setting certain read-only 
        /// properties such as CommentUrl and CommentStyle depending on whether CommentAPI 
        /// elements are contained in the table.</remarks>
        public Hashtable OptionalElements {
            get { return _optionalElements; }
            set { _optionalElements = null; }
        }


        private List<string> outgoingRelationships = new List<string>(); 
        /// <summary>
        /// Returns a collection of strings representing URIs to outgoing links in a feed. 
        /// </summary>
        public List<string> OutGoingLinks
        {
            get
            {
                return outgoingRelationships;
            }
            internal set
            {
                outgoingRelationships = value;
            }
        }



        /// <summary>
        /// Returns the feed object to which this item belongs
        /// </summary>
        public INewsFeed Feed
        {
            get
            {
                return this.myfeed;
            }
        }

        /// <summary>
        /// Converts the object to an XML string containing an RSS 2.0 item. 
        /// </summary>
        /// <param name="format">Indicates whether an XML representation of an 
        /// RSS item element is returned, an entire RSS feed with this item as its 
        /// sole item or an NNTP message.  </param>
        /// <returns></returns>
        public String ToString(NewsItemSerializationFormat format)
        {
            return ToString(format, true, false);
        }

        /// <summary>
        /// Converts the object to an XML string containing an RSS 2.0 item. 
        /// </summary>
        /// <param name="format">Indicates whether an XML representation of an 
        /// RSS item element is returned, an entire RSS feed with this item as its 
        /// sole item or an NNTP message. </param>
        /// <param name="useGMTDate">Indicates whether the date should be GMT or local time</param>
        /// <returns>A string representation of this news item</returns>		
        public String ToString(NewsItemSerializationFormat format, bool useGMTDate)
        {
            return ToString(format, useGMTDate, false);
        }


        /// <summary>
        /// Converts the object to an XML string containing an RSS 2.0 item. 
        /// </summary>
        /// <param name="format">Indicates whether an XML representation of an 
        /// RSS item element is returned, an entire RSS feed with this item as its 
        /// sole item or an NNTP message. </param>
        /// <param name="useGMTDate">Indicates whether the date should be GMT or local time</param>
        /// <param name="noDescriptions">Indicates whether the contents of RSS items should 
        /// be written out or not.</param>		
        /// <returns>A string representation of this news item</returns>		
        public String ToString(NewsItemSerializationFormat format, bool useGMTDate, bool noDescriptions)
        {
            string toReturn;

            switch (format)
            {
                case NewsItemSerializationFormat.NewsPaper:
                case NewsItemSerializationFormat.RssFeed:
                case NewsItemSerializationFormat.RssItem:
                    toReturn = ToRssFeedOrItem(format, useGMTDate, noDescriptions);
                    break;
                case NewsItemSerializationFormat.NntpMessage:
                    throw new NotSupportedException(format.ToString());
                    break;
                default:
                    throw new NotSupportedException(format.ToString());
            }


            return toReturn;
        }

        /// <summary>
        /// Converts the object to an XML string containing an RSS 2.0 item.  
        /// </summary>
        /// <returns></returns>
        public override String ToString()
        {
            return ToString(NewsItemSerializationFormat.RssItem);
        }

        /// <summary>
        /// Converts the NewsItem to an XML representation of an 
        /// RSS item element is returned or an entire RSS feed with this item as its 
        /// sole item.
        /// </summary>
        /// <param name="format">Indicates whether an XML representation of an 
        /// RSS item element is returned, an entire RSS feed with this item as its 
        /// sole item or an NNTP message. </param>
        /// <param name="useGMTDate">Indicates whether the date should be GMT or local time</param>		
        /// <param name="noDescriptions">Indicates whether the contents of RSS items should 
        /// be written out or not.</param>				
        /// <returns>An RSS item or RSS feed</returns>
        public String ToRssFeedOrItem(NewsItemSerializationFormat format, bool useGMTDate, bool noDescriptions)
        {
            StringBuilder sb = new StringBuilder("");
            XmlTextWriter writer = new XmlTextWriter(new StringWriter(sb));
            writer.Formatting = Formatting.Indented;

            if (format == NewsItemSerializationFormat.RssFeed || format == NewsItemSerializationFormat.NewsPaper)
            {
                if (format == NewsItemSerializationFormat.NewsPaper)
                {
                    writer.WriteStartElement("newspaper");
                    writer.WriteAttributeString("type", "newsitem");
                }
                else
                {
                    writer.WriteStartElement("rss");
                    writer.WriteAttributeString("version", "2.0");
                }

                writer.WriteStartElement("channel");
                writer.WriteElementString("title", FeedDetails.Title);
                writer.WriteElementString("link", FeedDetails.Link);
                writer.WriteElementString("description", FeedDetails.Description);

                foreach (string s in FeedDetails.OptionalElements.Values)
                {
                    writer.WriteRaw(s);
                }
            }

            WriteItem(writer, useGMTDate, noDescriptions);

            if (format == NewsItemSerializationFormat.RssFeed || format == NewsItemSerializationFormat.NewsPaper)
            {
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Helper function used by ToString(bool). 
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="useGMTDate">Indicates whether the time should be written out as GMT or local time</param>
        /// <param name="noDescriptions">Indicates whether the contents of RSS items should 
        /// be written out or not.</param>						
        public void WriteItem(XmlWriter writer, bool useGMTDate, bool noDescriptions)
        {
            //<item>
            writer.WriteStartElement("item");

            // xml:lang attribute
            if ((Language != null) && (Language.Length != 0))
            {
                writer.WriteAttributeString("xml", "lang", null, Language);
            }

            // <title />
            if ((Title != null) && (Title.Length != 0))
            {
                writer.WriteElementString("title", Title);
            }

            // <link /> 
            if ((HRef != null) && (HRef.Length != 0))
            {
                writer.WriteElementString("link", HRef);
            }

            // <pubDate /> 			we write it with InvariantInfo to get them stored in a non-localized format
            if (useGMTDate)
            {
                writer.WriteElementString("pubDate", Date.ToString("r", DateTimeFormatInfo.InvariantInfo));
            }
            else
            {
                writer.WriteElementString("pubDate", Date.ToLocalTime().ToString("F", DateTimeFormatInfo.InvariantInfo));
            }

            // <category />
            if ((Subject != null) && (Subject.Length != 0))
            {
                writer.WriteElementString("category", Subject);
            }

            //<guid>
            if ((Id != null) && (Id.Length != 0) && (Id.Equals(HRef) == false))
            {
                writer.WriteStartElement("guid");
                writer.WriteAttributeString("isPermaLink", "false");
                writer.WriteString(Id);
                writer.WriteEndElement();
            }

            //<dc:creator>
            if ((Author != null) && (Author.Length != 0))
            {
                writer.WriteElementString("creator", "http://purl.org/dc/elements/1.1/", Author);
            }

            //<annotate:reference>
            if ((ParentId != null) && (ParentId.Length != 0))
            {
                writer.WriteStartElement("annotate", "reference", "http://purl.org/rss/1.0/modules/annotate/");
                writer.WriteAttributeString("rdf", "resource", "http://www.w3.org/1999/02/22-rdf-syntax-ns#", ParentId);
                writer.WriteEndElement();
            }

            // Always prefer <description> 
            if (!noDescriptions && HasContent)
            {
                /* if(this.ContentType != ContentType.Xhtml){ */
                writer.WriteStartElement("description");
                writer.WriteCData(Content);
                writer.WriteEndElement();              
            }

            //<wfw:comment />
            if ((CommentUrl != null) && (CommentUrl.Length != 0))
            {
                if (CommentStyle == SupportedCommentStyle.CommentAPI)
                {
                    writer.WriteStartElement("wfw", "comment", RssHelper.NsCommentAPI);
                    writer.WriteString(CommentUrl);
                    writer.WriteEndElement();
                }
            }

            //<wfw:commentRss />
            if ((CommentRssUrl != null) && (CommentRssUrl.Length != 0))
            {
                writer.WriteStartElement("wfw", "commentRss", RssHelper.NsCommentAPI);
                writer.WriteString(CommentRssUrl);
                writer.WriteEndElement();
            }

            //<slash:comments>
            if (CommentCount != NewsItem.NoComments)
            {
                writer.WriteStartElement("slash", "comments", "http://purl.org/rss/1.0/modules/slash/");
                writer.WriteString(CommentCount.ToString());
                writer.WriteEndElement();
            }


            //	if(format == NewsItemSerializationFormat.NewsPaper){

            writer.WriteStartElement("fd", "state", "http://www.bradsoft.com/feeddemon/xmlns/1.0/");
            writer.WriteAttributeString("read", BeenRead ? "1" : "0");
            writer.WriteAttributeString("flagged", FlagStatus == Flagged.None ? "0" : "1");
            writer.WriteEndElement();

            //	} else { 
            //<rssbandit:flag-status />
            if (FlagStatus != Flagged.None)
            {
                //TODO: check: why we don't use the v2004/vCurrent namespace?
                writer.WriteElementString("flag-status", NamespaceCore.Feeds_v2003, FlagStatus.ToString());
            }
            //	}


            if (p_watchComments)
            {
                //TODO: check: why we don't use the v2004/vCurrent namespace?
                writer.WriteElementString("watch-comments", NamespaceCore.Feeds_v2003, "1");
            }

            if (HasNewComments)
            {
                //TODO: check: why we don't use the v2004/vCurrent namespace?
                writer.WriteElementString("has-new-comments", NamespaceCore.Feeds_v2003, "1");
            }

            //<enclosure />
            if (Enclosures != null)
            {
                foreach (IEnclosure enc in Enclosures)
                {
                    writer.WriteStartElement("enclosure");
                    writer.WriteAttributeString("url", enc.Url);
                    writer.WriteAttributeString("type", enc.MimeType);
                    writer.WriteAttributeString("length", enc.Length.ToString());
                    if (enc.Downloaded)
                    {
                        writer.WriteAttributeString("downloaded", "1");
                    }
                    if (enc.Duration != TimeSpan.MinValue)
                    {
                        writer.WriteAttributeString("duration", enc.Duration.ToString());
                    }
                    writer.WriteEndElement();
                }
            }

            //<rssbandit:outgoing-links />            
            writer.WriteStartElement("outgoing-links", NamespaceCore.Feeds_v2003);
            foreach (string outgoingLink in OutGoingLinks)
            {
                writer.WriteElementString("link", NamespaceCore.Feeds_v2003, outgoingLink);
            }
            writer.WriteEndElement();

            /* everything else */
            foreach (string s in OptionalElements.Values)
            {
                writer.WriteRaw(s);
            }

            //end </item> 
            writer.WriteEndElement();
        }


        #endregion 

        #region IRelation implementation 

        /// <summary>
        /// Return a web reference, a resource ID, mail/message ID, NNTP post ID.
        /// </summary>
        public string HRef
        {
            get { return this.Link; }
        }

        /// <summary>
        /// Return a list of outgoing Relation objects, e.g. 
        /// links the current relation resource points to.
        /// </summary>
        public IList<string> OutgoingRelations
        {
            get
            {
                return outgoingRelationships;
            }
        }

        /// <summary>
        /// The DateTime the item was published/updated. It should be specified as UTC.
        /// </summary>
        /// <remarks>This property is read only</remarks>
        public virtual DateTime PointInTime
        {
            get { return this.Date; }
            set { /* can't set IFeedItem.PubDate */ }
        }

        /// <summary>
        /// Return true, if the Relation has some external relations (that are not part
        /// of the RelationCosmos). Default is false;
        /// </summary>
        public virtual bool HasExternalRelations { get { return false; } }

        /// <summary>
        /// Gets called if <see cref="HasExternalRelations">HasExternalRelations</see>
        /// returns true to retrieve the external Relation resource(s).
        /// Default return is the RelationCosmos.EmptyRelationList.
        /// </summary>
        public virtual IList<IRelation> GetExternalRelations()
        {
                return GetList<IRelation>.Empty;
        }
        /// <summary>
        /// Should be overridden. Stores a collection of external Relations related
        /// to this RelationBase.
        /// </summary>
        public virtual void SetExternalRelations<T>(IList<T> relations) where T: IRelation
        {
            /* not supported for Windows RSS items */ 
        }

        #endregion 


        #region ICloneable implementation

        /// <summary>
        /// Returns a copy of this object
        /// </summary>
        /// <returns>A copy of this object</returns>
        public object Clone()
        {
            return new WindowsRssNewsItem(this.myitem, this.myfeed); 
        }

        /// <summary>
        /// Copies the item (clone) and set the new parent to the provided feed 
        /// </summary>
        /// <param name="f">NewsFeed</param>
        /// <returns></returns>
        public INewsItem Clone(INewsFeed f)
        {
            //BUGBUG: This will throw exceptions when used as part of flagging or watching items. Instead return NewsItem instance
            return new WindowsRssNewsItem(this.myitem, f as WindowsRssNewsFeed); 
        }

        #endregion 

        #region IEquatable implementation 

        /// <summary>
        /// Compares to see if two WindowsRssNewsItems are identical. 
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as INewsItem);
        }

        public bool Equals(INewsItem other)
        {
            return Equals(other as WindowsRssNewsItem);
        }

        /// <summary>
        /// Tests if this item is the same as another. The item to test. 
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Equals(WindowsRssNewsItem item) {
            if (item == null)
                return false;

            return item.myfeed.id.Equals(this.myfeed.id) && item.Id.Equals(this.Id); 
        }

        /// <summary>
        /// Returns a hashcode for the given item
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return this.Id.GetHashCode();
        }

        #endregion 

        #region IComparable implementation

        /// <summary>
        /// Impl. IComparable.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public int CompareTo(object obj)
        {
            return CompareTo(obj as WindowsRssNewsItem);
        }

        public int CompareTo(WindowsRssNewsItem other)
        {
            if (ReferenceEquals(this, other))
                return 0;

            if (ReferenceEquals(other, null))
                return 1;

            return this.Date.CompareTo(other.Date);
        }

        public int CompareTo(IRelation other)
        {
            return CompareTo(other as WindowsRssNewsItem);
        }

        #endregion

        #region IXPathNavigable implementation 

        /// <summary>
        /// Creates an XPathNavigator over this object
        /// </summary>
        /// <returns></returns>
        public XPathNavigator CreateNavigator()
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(this.myitem.Xml(FEEDS_XML_INCLUDE_FLAGS.FXIF_NONE));
            return doc.CreateNavigator(); 
        }

        #endregion 

    }

    #endregion 

    #region WindowsRssEnclosure

    /// <summary>
    /// Represents an enclosure from the Windows RSS platform
    /// </summary>
    public class WindowsRssEnclosure : IEnclosure
    {

        #region constructors 

           /// <summary>
        /// We always want an associated IFeedItem instance
        /// </summary>
        private WindowsRssEnclosure() { ;}

          /// <summary>
        /// Initializes the class
        /// </summary>
        /// <param name="enclosure">The IFeedEnclosure instance that this object will wrap</param>
        public WindowsRssEnclosure(IFeedEnclosure enclosure)
        {
            if (enclosure == null) throw new ArgumentNullException("enclosure");
            this.myenclosure = enclosure;
          
            //TODO: RelationCosmos and outgoing links processing? 
        }

        #endregion 

        #region private fields

        /// <summary>
        /// The IFeedEnclosure instance wrapped by this object
        /// </summary>
        private IFeedEnclosure myenclosure = null; 

        #endregion 



        #region IEnclosure implementation 

        /// <summary>
        /// The MIME type of the enclosure
        /// </summary>
        public string MimeType
        {
            get { return myenclosure.Type; }
        }

        /// <summary>
        /// The length of the enclosure in bytes
        /// </summary>
        public long Length
        {
            get { return myenclosure.Length; }
        }

        /// <summary>
        /// The MIME type of the enclosure
        /// </summary>
        public string Url
        {
            get { return myenclosure.DownloadUrl; }
        }

        /// <summary>
        /// The description associated with the item obtained via itunes:subtitle or media:title
        /// </summary>
        /// <remarks>This field is read only</remarks>
        public string Description {
            get { return null; }
            set { ; }
        }

        private bool _downloaded;
        /// <summary>
        /// Indicates whether this enclosure has already been downloaded or not.
        /// </summary>
        public bool Downloaded
        {
            get { return _downloaded; }
            set { _downloaded = value;  }
        }


        /// <summary>
        /// Gets the playing time of the enclosure. 
        /// </summary>
        /// <remarks>This field is read only</remarks>
        public TimeSpan Duration
        {
            get { return TimeSpan.MinValue; }
            set { /* */ }
        }

        #endregion 

        #region IEquatable implementation 

        /// <summary>
        /// Compares to see if two Enclosures are identical. 
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as WindowsRssEnclosure);
        }

        /// <summary>
        /// Compares to see if two WindowsRssEnclosure are identical. Identity just checks to see if they have 
        /// the same link, 
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public bool Equals(IEnclosure item)
        {
            if (item == null)
            {
                return false;
            }

            if (ReferenceEquals(this, item))
            {
                return true;
            }

            if (String.Compare(Url, item.Url) == 0)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get the hash code of the object
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            if (string.IsNullOrEmpty(Url))
            {
                return String.Empty.GetHashCode();
            }
            else
            {
                return Url.GetHashCode();
            }
        }

        #endregion
    }

    #endregion 

    #region WindowsRssNewsFeed

    /// <summary>
    /// Represents a NewsFeed obtained from the Windows RSS platform
    /// </summary>
    public class WindowsRssNewsFeed : INewsFeed, IDisposable, IFeedDetails
    {

        #region constructors 

        /// <summary>
        /// We always want an associated IFeed instance
        /// </summary>
        private WindowsRssNewsFeed() { ;}

        /// <summary>
        /// Initializes the class
        /// </summary>
        /// <param name="feed">The IFeed instance that this object will wrap</param>
        public WindowsRssNewsFeed(IFeed feed) {
            if (feed == null) throw new ArgumentNullException("feed"); 
            this.myfeed = feed;
            
            /* do this here because COM interop is too slow to check it each time property is accessed */
            this._id = myfeed.LocalId; 
            
            //make sure we have a list of items ready to go
            this.LoadItemsList();             
        }

        /// <summary>
        /// Initializes the class
        /// </summary>
        /// <param name="feed">The IFeed instance that this object will wrap</param>
        /// <param name="banditfeed">The object that contains the settings that will be used to initialize this class</param>
        public WindowsRssNewsFeed(IFeed feed, INewsFeed banditfeed): this(feed)
        {
            if (banditfeed != null)
            {
                this.refreshrate = banditfeed.refreshrate;
                this.refreshrateSpecified = banditfeed.refreshrateSpecified;
                this.maxitemage = banditfeed.maxitemage;
                this.markitemsreadonexit = banditfeed.markitemsreadonexit;
                this.markitemsreadonexitSpecified = banditfeed.markitemsreadonexitSpecified;
                this.listviewlayout = banditfeed.listviewlayout;
                this.favicon = banditfeed.favicon;
                this.stylesheet = banditfeed.stylesheet;
                this.enclosurealert = banditfeed.enclosurealert;
                this.enclosurealertSpecified = banditfeed.enclosurealertSpecified;
                this.alertEnabled = banditfeed.alertEnabled;
                this.alertEnabledSpecified = banditfeed.alertEnabledSpecified;
                this.Any = banditfeed.Any;
                this.AnyAttr = banditfeed.AnyAttr;
            }
        }

        #endregion 

        #region destructor and IDisposable implementation 

        /// <summary>
        /// Releases the associated COM objects
        /// </summary>
        /// <seealso cref="myfeed"/>
        ~WindowsRssNewsFeed() {
            Dispose(false);           
        }

        /// <summary>
        /// Disposes of the class
        /// </summary>
        public void Dispose()
        {

            if (!disposed)
            {
                Dispose(true);
            }

        }

        /// <summary>
        /// Disposes of the class
        /// </summary>
        /// <param name="disposing"></param>
        public void Dispose(bool disposing)
        {
            lock (this)
            {                
                if (myfeed != null)
                {
                    Marshal.ReleaseComObject(myfeed);
                }
                System.GC.SuppressFinalize(this);
                disposed = true; 
            }
        }

        #endregion


        #region private fields

        /// <summary>
        /// Indicates that the object has been disposed
        /// </summary>
        private bool disposed = false;

        /// <summary>
        /// The actual IFeed instance that this object is wrapping
        /// </summary>
        private IFeed myfeed = null;

        /// <summary>
        /// The list of WindowsRssNewsItem instances contained by this feed. 
        /// </summary>
        private List<INewsItem> items = new List<INewsItem>(); 

        #endregion

        #region private methods

        /// <summary>
        /// Loads items from the underlying Windows RSS platform into the ItemsList property
        /// </summary>
        /// <seealso cref="ItemsList"/>
        internal void LoadItemsList()
        {
            this.items.Clear();
            IFeedsEnum feedItems = this.myfeed.Items as IFeedsEnum;

            foreach (IFeedItem item in feedItems)
            {
                this.items.Add(new WindowsRssNewsItem(item, this));
            }
            Console.WriteLine("LOAD_ITEMS_LIST:'{0}' loaded {1} item(s)", myfeed.Path, items.Count); 
        }

        #endregion 

        #region public methods

        /// <summary>
        /// Sets the IFeed object represented by this object
        /// </summary>
        /// <param name="feed">The IFeed instance</param>
        internal void SetIFeed(IFeed feed) {
            if (feed != null)
            {
                lock (this)
                {
                    if (myfeed != null)
                    {
                        Marshal.ReleaseComObject(myfeed);
                    }
                    myfeed = feed;
                }
            }        
        }

        #endregion 

        #region INewsFeed implementation

        public string title
        {
            get { return myfeed.Name; }
          
            set
            {
              if(!StringHelper.EmptyTrimOrNull(value))
              { 
                  myfeed.Rename(value);
                  OnPropertyChanged("title");
              }
            }
        }

        /// <summary>
        /// Setting this value does nothing. 
        /// </summary>
        [XmlElement(DataType = "anyURI")]
        public string link
        {
            get
            {
                try
                {
                    return myfeed.DownloadUrl;
                }
                catch (COMException) /* thrown if the feed has never been downloaded */ 
                {
                    return myfeed.Url; 
                }

            }

            set
            {
                /* can't set IFeed.DownloadUrl */
            }
        }

        private string _id; 
        /// <summary>
        /// Setting this value does nothing. 
        /// </summary>
        [XmlAttribute]
        public string id
        {
            get { return _id; }

            set
            {
              /* can't set IFeed.LocalId */
            }
        }

        /// <summary>
        /// Setting this value does nothing. 
        /// </summary>
        [XmlElement("last-retrieved")]
        public DateTime lastretrieved
        {
            get { return myfeed.LastDownloadTime; }
            set
            {
                /* can't set myfeed.LastDownloadTime */
            }
        }

        /// <summary>
        /// Setting this value does nothing. 
        /// </summary>
        [XmlIgnore]
        public bool lastretrievedSpecified
        {
            get
            {
                return true;
            }

            set
            { 
                /* it should always be set */
            }
        }


        /// <remarks/>
        [XmlElement("refresh-rate")]
        public int refreshrate { get; set; }
     
        /// <remarks/>
        [XmlIgnore]
        public bool refreshrateSpecified { get; set; }

        /// <remarks>This property does not apply to this object </remarks>
        public string etag { get { return null; } set { /* not applicable to this type */ } }

        /// <remarks>This property does not apply to this object </remarks>
        [XmlElement(DataType = "anyURI")]
        public string cacheurl { get { return null; } set { /* not applicable to this type */ } }

        /// <remarks/>
        [XmlElement("max-item-age", DataType = "duration")]
        public string maxitemage { get; set; }


        private List<string> _storiesrecentlyviewed = new List<string>();
        /// <remarks/>
        [XmlArray(ElementName = "stories-recently-viewed", IsNullable = false)]
        [XmlArrayItem("story", Type = typeof(String), IsNullable = false)]
        public List<string> storiesrecentlyviewed
        {
            get
            {
                //TODO: Can we make this less expensive. Current implementation tries to be careful in case
                // items marked as read outside RSS Bandit
                _storiesrecentlyviewed.Clear(); 
                IFeedsEnum items = myfeed.Items as IFeedsEnum;
                foreach (IFeedItem item in items)
                {
                    if (item.IsRead)
                    {
                        _storiesrecentlyviewed.Add(item.LocalId.ToString());
                    }
                } 
                return _storiesrecentlyviewed;
            }
            set
            {
                _storiesrecentlyviewed = new List<string>(value);
            }
        }

        private List<string> _deletedstories = new List<string>();
        /// <remarks/>
        [XmlArray(ElementName = "deleted-stories", IsNullable = false)]
        [XmlArrayItem("story", Type = typeof(String), IsNullable = false)]
        public List<string> deletedstories
        {
            get
            {
                return _deletedstories;
            }
            set
            {
                _deletedstories = new List<string>(value);
            }
        }

        /// <summary>
        /// Setting this value does nothing. 
        /// </summary>
        [XmlElement("if-modified-since")]
        public DateTime lastmodified {
            get
            {
                return myfeed.LastWriteTime;
            }
            set
            {
                /* can't set IFeed.LastWriteTime */
            }
        }

        /// <summary>
        /// Setting this value does nothing. 
        /// </summary>
        [XmlIgnore]
        public bool lastmodifiedSpecified { get { return true; } set { } }

        /// <remarks>Not supported by this object</remarks>
        [XmlElement("auth-user")]
        public string authUser { get { return null; } set { } }

        /// <remarks>Not supported by this object</remarks>       
        [XmlElement("auth-password", DataType = "base64Binary")]
        public Byte[] authPassword { get { return null; } set { } }

        /// <remarks/>
        [XmlElement("listview-layout")]
        public string listviewlayout { get; set; }

        private string _favicon;
        /// <remarks/>
        public string favicon
        {
            get
            {
                return _favicon;
            }

            set
            {
                if (String.IsNullOrEmpty(_favicon) || !_favicon.Equals(value))
                {
                    _favicon = value;
                    this.OnPropertyChanged("favicon");
                }
            }
        }




        /// <remarks/>
        [XmlElement("download-enclosures")]
        public bool downloadenclosures
        {
            get
            {
                return myfeed.DownloadEnclosuresAutomatically;
            }

            set
            {
                myfeed.DownloadEnclosuresAutomatically = value;
            }
        }

        /// <summary>
        /// Setting this value does nothing. 
        /// </summary>
        [XmlIgnore]
        public bool downloadenclosuresSpecified { get { return true; } set { /* it is always set */ } }

        /// <summary>
        /// Setting this value does nothing. 
        /// </summary>
        [XmlElement("enclosure-folder")]
        public string enclosurefolder
        {
            get
            {
                return myfeed.LocalEnclosurePath;
            }

            set
            {
             /* IFeed.LocalEnclosurePath can't be set */ 
            }
        }

        /// <summary>
        /// Setting this value does nothing. 
        /// </summary>
        [XmlAttribute("replace-items-on-refresh")]
        public bool replaceitemsonrefresh
        {
            get
            {
                try
                {
                    return myfeed.IsList;
                }
                catch
                {
                    return false; 
                }
            }
            set
            {
                /* IFeed.IsList can't be set */ 
            }
        }

        /// <summary>
        /// Setting this value does nothing. 
        /// </summary>
        [XmlIgnore]
        public bool replaceitemsonrefreshSpecified { get { return true; } set { } }

      
        public string stylesheet { get; set; }

        /// <remarks>Reference the corresponding NntpServerDefinition</remarks>
        [XmlElement("news-account")]
        public string newsaccount { get; set; }

        /// <remarks/>
        [XmlElement("mark-items-read-on-exit")]
        public bool markitemsreadonexit { get; set; }

        /// <remarks/>
        [XmlIgnore]
        public bool markitemsreadonexitSpecified { get; set; }

        /// <remarks/>
        [XmlAnyElement]
        public XmlElement[] Any { get; set; }


        /// <remarks/>
        [XmlAttribute("alert"), DefaultValue(false)]
        public bool alertEnabled { get; set; }

        /// <remarks/>
        [XmlIgnore]
        public bool alertEnabledSpecified { get; set; }


        /// <remarks/>
        [XmlAttribute("enclosure-alert"), DefaultValue(false)]
        public bool enclosurealert { get; set; }

        /// <remarks/>
        [XmlIgnore]
        public bool enclosurealertSpecified { get; set; }


        //TODO: Make this a collection
        /// <remarks/>
        [XmlAttribute]
        public string category {

            get
            {
                IFeedFolder myfolder = myfeed.Parent as IFeedFolder;

                if (myfolder != null)
                {
                    return myfolder.Path;
                }
                else
                {
                    return null;
                }
            }

            set
            {
                if (!StringHelper.EmptyTrimOrNull(value) && !value.Equals(this.category))
                {
                    WindowsRssFeedSource handler = owner as WindowsRssFeedSource;
                    handler.ChangeCategory(this, handler.AddCategory(value)); 
                }
            }
        
        }

        /// <remarks/>
        [XmlAnyAttribute]
        public XmlAttribute[] AnyAttr { get; set; }

        /// <remarks>True, if the feed caused an exception on request to prevent sequenced
        /// error reports on every automatic download</remarks>
        [XmlIgnore]
        public bool causedException
        {
            get
            {
                return causedExceptionCount != 0;
            }
            set
            {
                if (value)
                {
                    causedExceptionCount++; // raise counter
                    lastretrievedSpecified = true;
                    lastretrieved = new DateTime(DateTime.Now.Ticks);
                }
                else
                    causedExceptionCount = 0; // reset
            }
        }

        /// <remarks>Number of exceptions caused on requests</remarks>
        [XmlIgnore]
        public int causedExceptionCount { get; set; }

        /// <remarks>Can be used to store any attached data</remarks>
        [XmlIgnore]
        public object Tag { get; set; }

        /// <remarks/>
        [XmlIgnore]
        public bool containsNewMessages
        {
            get
            {
                bool hasUnread = items.Any(item => item.BeenRead == false);
                return hasUnread; 
            }

            set
            {
                /* This value is always correct */ 
            }
        }

        private bool _containsNewComments;
        /// <remarks/>
        [XmlIgnore]
        public bool containsNewComments
        {
            get
            {
                return _containsNewComments;
            }

            set
            {
                if (!_containsNewComments.Equals(value))
                {
                    _containsNewComments = value;
                    this.OnPropertyChanged("containsNewComments");
                }
            }
        }

        /// <remarks />                
        [XmlIgnore]
        public object owner { get; set; }

        /// <summary>
        /// Gets the value of a particular wildcard element. If the element is not found then 
        /// null is returned
        /// </summary>
        /// <param name="namespaceUri"></param>
        /// <param name="localName"></param>
        /// <returns>The value of the wildcard element obtained by calling XmlElement.InnerText
        /// or null if the element is not found. </returns>
        public string GetElementWildCardValue(string namespaceUri, string localName)
        {
            foreach (XmlElement element in Any)
            {
                if (element.LocalName == localName && element.NamespaceURI == namespaceUri)
                    return element.InnerText;
            }
            return null;
        }

        /// <summary>
        /// Removes an entry from the storiesrecentlyviewed collection
        /// </summary>
        /// <seealso cref="storiesrecentlyviewed"/>
        /// <param name="storyid">The ID to add</param>
        public void AddViewedStory(string storyid)
        {
            if (!_storiesrecentlyviewed.Contains(storyid))
            {
                _storiesrecentlyviewed.Add(storyid);
                if (null != PropertyChanged)
                {
                    this.OnPropertyChanged(new CollectionChangedEventArgs("storiesrecentlyviewed", CollectionChangeAction.Add, storyid));
                }
            }
        }

        /// <summary>
        /// Adds an entry to the storiesrecentlyviewed collection
        /// </summary>
        /// <seealso cref="storiesrecentlyviewed"/>
        /// <param name="storyid">The ID to remove</param>
        public void RemoveViewedStory(string storyid)
        {
            if (_storiesrecentlyviewed.Contains(storyid))
            {
                _storiesrecentlyviewed.Remove(storyid);
                if (null != PropertyChanged)
                {
                    this.OnPropertyChanged(new CollectionChangedEventArgs("storiesrecentlyviewed", CollectionChangeAction.Remove, storyid));
                }
            }
        }

        /// <summary>
        /// Removes an entry from the deletedstories collection
        /// </summary>
        /// <seealso cref="deletedstories"/>
        /// <param name="storyid">The ID to add</param>
        public void AddDeletedStory(string storyid)
        {
            if (!_deletedstories.Contains(storyid))
            {
                _deletedstories.Add(storyid);
                if (null != PropertyChanged)
                {
                    this.OnPropertyChanged(new CollectionChangedEventArgs("deletedstories", CollectionChangeAction.Add, storyid));
                }
            }
        }

        /// <summary>
        /// Adds an entry to the deletedstories collection
        /// </summary>
        /// <seealso cref="deletedstories"/>
        /// <param name="storyid">The ID to remove</param>
        public void RemoveDeletedStory(string storyid)
        {
            if (_deletedstories.Contains(storyid))
            {
                _deletedstories.Remove(storyid);
                if (null != PropertyChanged)
                {
                    this.OnPropertyChanged(new CollectionChangedEventArgs("deletedstories", CollectionChangeAction.Remove, storyid));
                }
            }

        }


        #endregion 

        #region IFeedDetails implementation 

        /// <summary>Gets the Feed Language</summary>
        public string Language { 
            get { return this.myfeed.Language; }
        }

        /// <summary>Gets the Feed Title</summary>
        public string Title {
            get { return this.myfeed.Title; }
        }

        /// <summary>Gets the Feed Homepage Link</summary>
        public string Link {
            get { return this.myfeed.Link; }
        }

        /// <summary>Gets the Feed Description</summary>
        public string Description {
            get { return this.myfeed.Description; }
        }


        /// <summary>
        /// The list of news items belonging to the feed
        /// </summary>
        public List<INewsItem> ItemsList
        {

            get
            {
                lock (this.items)
                {
                    LoadItemsList(); 
                }

                return this.items;
            }
            set
            {
                /* Can't set IFeed.Items */
            }

        }

        private Dictionary<XmlQualifiedName, string> _optionalElements = new Dictionary<XmlQualifiedName, string>(); 
        /// <summary>Gets the optional elements found at Feed level</summary>	  
        public Dictionary<XmlQualifiedName, string> OptionalElements {
            get { return this._optionalElements; } 
        }

        /// <summary>
        /// Gets the type of the FeedDetails info
        /// </summary>
        public FeedType Type {
            get { return FeedType.Rss; }
        }

        /// <summary>
        /// The unique identifier for the feed
        /// </summary>
        string IFeedDetails.Id
        {
            get { return this.id; }

            set
            {
                /* can't set IFeed.LocalId */
            }
        }

        /// <summary>
        /// Returns a copy of this object
        /// </summary>
        /// <returns>A copy of this object</returns>
        public object Clone()
        {
            return new WindowsRssNewsFeed(myfeed);
        }

        /// <summary>
        /// Writes this object as an RSS 2.0 feed to the specified writer
        /// </summary>
        /// <param name="writer"></param>
        public void WriteTo(XmlWriter writer)
        {
            this.WriteTo(writer, NewsItemSerializationFormat.RssFeed, true);
        }


        /// <summary>
        /// Writes this object as an RSS 2.0 feed to the specified writer
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="format">indicates whether we are writing a FeedDemon newspaper or an RSS feed</param>
        public void WriteTo(XmlWriter writer, NewsItemSerializationFormat format)
        {
            this.WriteTo(writer, format, true);
        }

        /// <summary>
        /// Writes this object as an RSS 2.0 feed to the specified writer
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="format">indicates whether we are writing a FeedDemon newspaper or an RSS feed</param>
        /// <param name="useGMTDate">Indicates whether the date should be GMT or local time</param>				
        public void WriteTo(XmlWriter writer, NewsItemSerializationFormat format, bool useGMTDate)
        {
            //writer.WriteStartDocument(); 

            if (format == NewsItemSerializationFormat.NewsPaper)
            {
                //<newspaper type="channel">
                writer.WriteStartElement("newspaper");
                writer.WriteAttributeString("type", "channel");
                writer.WriteElementString("title", this.title);
            }
            else if (format != NewsItemSerializationFormat.Channel)
            {
                //<rss version="2.0">
                writer.WriteStartElement("rss");
                writer.WriteAttributeString("version", "2.0");
            }
            
            //<channel>
            writer.WriteStartElement("channel");

            //<title />
            writer.WriteElementString("title", this.Title);

            //<link /> 
            writer.WriteElementString("link", this.Link);

            //<description /> 
            writer.WriteElementString("description", this.Description);

            //other stuff
            foreach (string s in this.OptionalElements.Values)
            {
                writer.WriteRaw(s);
            }

            //<item />
            foreach (INewsItem item in this.ItemsList)
            {
                writer.WriteRaw(item.ToString(NewsItemSerializationFormat.RssItem, true));
            }

            writer.WriteEndElement();

            if (format != NewsItemSerializationFormat.Channel)
            {
                writer.WriteEndElement();
            }

            //writer.WriteEndDocument(); 
        }

        /// <summary>
        /// Provides the XML representation of the feed as an RSS 2.0 feed. 
        /// </summary>
        /// <param name="format">Indicates whether the XML should be returned as an RSS feed or a newspaper view</param>
        /// <returns>the feed as an XML string</returns>
        public string ToString(NewsItemSerializationFormat format)
        {
            return this.ToString(format, true);
        }

        /// <summary>
        /// Provides the XML representation of the feed as an RSS 2.0 feed. 
        /// </summary>
        /// <param name="format">Indicates whether the XML should be returned as an RSS feed or a newspaper view</param>
        /// <param name="useGMTDate">Indicates whether the date should be GMT or local time</param>
        /// <returns>the feed as an XML string</returns>
        public string ToString(NewsItemSerializationFormat format, bool useGMTDate)
        {

            StringBuilder sb = new StringBuilder("");
            XmlTextWriter writer = new XmlTextWriter(new StringWriter(sb));

            this.WriteTo(writer, format, useGMTDate);

            writer.Flush();
            writer.Close();

            return sb.ToString();

        }


        #endregion 

        #region INotifyPropertyChanged implementation

        /// <summary>
        ///  Occurs when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Fired whenever a property is changed. 
        /// </summary>
        /// <param name="propertyName"></param>
        protected virtual void OnPropertyChanged(string propertyName)
        {
            OnPropertyChanged(DataBindingHelper.GetPropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Notifies listeners that a property has changed. 
        /// </summary>
        /// <param name="e">Details on the property change event</param>
        protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (null != PropertyChanged)
            {
                PropertyChanged(this, e);
            }
        }


        #endregion 

        #region public methods

        /// <summary>
        /// Asynchronously downloads the feed using the Windows RSS platform
        /// </summary>
        public void RefreshFeed()
        {
            this.myfeed.Download(); 
        }

        #endregion 
    }

    #endregion

    #region WindowsRssNewsFeedCategory

    public class WindowsRssNewsFeedCategory : INewsFeedCategory, IDisposable 
    {

        #region constructor 

        /// <summary>
        /// This class must always represent an instance of IFeedFolder 
        /// </summary>
        private WindowsRssNewsFeedCategory() { ;} 

         /// <summary>
        /// Initializes the class
        /// </summary>
        /// <param name="folder">The IFeed instance that this object will wrap</param>
        public WindowsRssNewsFeedCategory(IFeedFolder folder)
        {
            if (folder == null) throw new ArgumentNullException("folder"); 
            this.myfolder = folder; 
        }
       
        
        /// <summary>
        /// Initializes the class
        /// </summary>
        /// <param name="folder">The IFeed instance that this object will wrap</param>
        /// <param name="category">A category instance from which this object shall obtain the values for it's INewsFeedCategory properties</param>
        public WindowsRssNewsFeedCategory(IFeedFolder folder, INewsFeedCategory category)
            : this(folder)
        {
            if (category != null)
            {
                this.AnyAttr = category.AnyAttr;
                this.downloadenclosures = category.downloadenclosures;
                this.downloadenclosuresSpecified = category.downloadenclosuresSpecified;
                this.enclosurealert = category.enclosurealert;
                this.enclosurealertSpecified = category.enclosurealertSpecified;
                this.listviewlayout = category.listviewlayout;
                this.markitemsreadonexit = category.markitemsreadonexit;
                this.markitemsreadonexitSpecified = category.markitemsreadonexitSpecified;
                this.maxitemage = category.maxitemage;
                this.refreshrate = category.refreshrate;
                this.refreshrateSpecified = category.refreshrateSpecified;
                this.stylesheet = category.stylesheet;
            }
        }

        #endregion 

        #region private fields

        /// <summary>
        /// Indicates that the object has been disposed
        /// </summary>
        private bool disposed = false;

        /// <summary>
        /// The actual IFeedFolder instance that this object is wrapping
        /// </summary>
        private IFeedFolder myfolder = null;

        #endregion

        #region destructor and IDisposable implementation 

        /// <summary>
        /// Releases the associated COM objects
        /// </summary>
        /// <seealso cref="myfeed"/>
        ~WindowsRssNewsFeedCategory()
        {
            Dispose(false);           
        }

        /// <summary>
        /// Disposes of the class
        /// </summary>
        public void Dispose()
        {

            if (!disposed)
            {
                Dispose(true);
            }

        }

        /// <summary>
        /// Disposes of the class
        /// </summary>
        /// <param name="disposing"></param>
        public void Dispose(bool disposing)
        {
            lock (this)
            {                
                if (myfolder != null)
                {
                    Marshal.ReleaseComObject(myfolder);
                }
                System.GC.SuppressFinalize(this);
                disposed = true; 
            }
        }

        #endregion

        #region INewsFeedCategory implementation 

        /// <remarks/>
        [XmlAttribute("mark-items-read-on-exit")]
        public bool markitemsreadonexit { get; set; }

        /// <remarks/>
        [XmlIgnore]
        public bool markitemsreadonexitSpecified { get; set; }

        /// <remarks/>
        [XmlAttribute("download-enclosures")]
        public bool downloadenclosures { get; set; }

        /// <remarks/>
        [XmlIgnore]
        public bool downloadenclosuresSpecified { get; set; }

        /// <remarks>This property is not supported by this object</remarks>
        [XmlAttribute("enclosure-folder")]
        public string enclosurefolder { get { return null; } set { /* */} }

        ///<summary>ID to an FeedColumnLayout</summary>
        /// <remarks/>
        [XmlAttribute("listview-layout")]
        public string listviewlayout { get; set; }

        /// <remarks/>
        [XmlAttribute]
        public string stylesheet { get; set; }

        /// <remarks/>
        [XmlAttribute("refresh-rate")]
        public int refreshrate { get; set; }

        /// <remarks/>
        [XmlIgnore]
        public bool refreshrateSpecified { get; set; }

        /// <remarks/>
        [XmlAttribute("max-item-age", DataType = "duration")]
        public string maxitemage { get; set; }

        /// <remarks/>
        [XmlText]
        public string Value
        {
            get
            {
                return myfolder.Path;
            }
            set 
            {
                if (!StringHelper.EmptyTrimOrNull(value))
                {
                    myfolder.Rename(value);
                }
            }
        }

        /// <remarks/>
        [XmlIgnore]
        public INewsFeedCategory parent { get; set; }

        /// <remarks/>
        [XmlAttribute("enclosure-alert"), DefaultValue(false)]
        public bool enclosurealert { get; set; }

        /// <remarks/>
        [XmlIgnore]
        public bool enclosurealertSpecified { get; set; }

        /// <remarks/>
        [XmlAnyAttribute]
        public XmlAttribute[] AnyAttr { get; set; }


        #endregion 

        #region Equality methods

        /// <summary>
        /// Tests to see if two category objects represent the same feed. 
        /// </summary>
        /// <returns></returns>
        public override bool Equals(Object obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            WindowsRssNewsFeedCategory c = obj as WindowsRssNewsFeedCategory;

            if (c == null)
            {
                return false;
            }

            return this.myfolder.Path.Equals(c.myfolder.Path);
        }

        /// <summary>
        /// Returns a hashcode for a category object. 
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return this.myfolder.Path.GetHashCode();
        }

        #endregion
    }

    #endregion 
}

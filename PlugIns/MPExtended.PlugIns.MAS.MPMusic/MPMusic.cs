﻿#region Copyright (C) 2011 MPExtended
// Copyright (C) 2011 MPExtended Developers, http://mpextended.github.com/
// 
// MPExtended is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 2 of the License, or
// (at your option) any later version.
// 
// MPExtended is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with MPExtended. If not, see <http://www.gnu.org/licenses/>.
#endregion

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using MPExtended.Services.MediaAccessService.Interfaces;
using MPExtended.Services.MediaAccessService.Interfaces.Music;
using MPExtended.Services.MediaAccessService.Interfaces.Shared;
using System.Data.SQLite;
using MPExtended.Libraries.SQLitePlugin;
using System.Security.Cryptography;

namespace MPExtended.PlugIns.MAS.MPMusic
{
    [Export(typeof(IMusicLibrary))]
    [ExportMetadata("Name", "MP MyMusic")]
    [ExportMetadata("Type", typeof(MPMusic))]
    public class MPMusic : Database, IMusicLibrary
    {
        private IPluginData data;
        private MD5 md5;

        [ImportingConstructor]
        public MPMusic(IPluginData data)
        {
            this.data = data;
        }

        public void Init()
        {
            DatabasePath = data.Configuration["database"];
        }

        private LazyQuery<T> LoadAllTracks<T>() where T : WebMusicTrackBasic, new()
        {
            Dictionary<string, WebMusicArtistBasic> artists = GetAllArtists().ToDictionary(x => x.Id, x => x);


            string sql = "SELECT idTrack, strAlbumArtist, strAlbum, strArtist, iTrack, strTitle, strPath, iDuration, iYear, strGenre " +
                         "FROM tracks t " + 
                         "WHERE %where";
            return new LazyQuery<T>(this, sql, new List<SQLFieldMapping>() {
                new SQLFieldMapping("", "idTrack", "Id", DataReaders.ReadIntAsString),
                new SQLFieldMapping("", "strArtist", "Artist", DataReaders.ReadPipeList),
                new SQLFieldMapping("", "strArtist", "ArtistId", ArtistIdReader),
                new SQLFieldMapping("", "strAlbum", "Album", DataReaders.ReadString),
                new SQLFieldMapping("", "strAlbum", "AlbumId", AlbumIdReader),
                new SQLFieldMapping("", "strTitle", "Title", DataReaders.ReadString),
                new SQLFieldMapping("", "iTrack", "TrackNumber", DataReaders.ReadInt32),
                new SQLFieldMapping("", "strPath", "Path", DataReaders.ReadStringAsList),
                new SQLFieldMapping("", "strGenre", "Genres", DataReaders.ReadPipeList),
                new SQLFieldMapping("", "iYear", "Year", DataReaders.ReadInt32),
                new SQLFieldMapping("", "dateAdded", "DateAdded", DataReaders.ReadDateTime)
            }, delegate(T item)
            {
                if (item is WebMusicTrackDetailed)
                {
                    WebMusicTrackDetailed det = item as WebMusicTrackDetailed;
                    det.Artists = det.ArtistId.Where(x => artists.ContainsKey(x)).Select(x => artists[x]).ToList();
                }
                return item;
            });
        }

        public IEnumerable<WebMusicTrackBasic> GetAllTracks()
        {
            return LoadAllTracks<WebMusicTrackBasic>();
        }

        public IEnumerable<WebMusicTrackDetailed> GetAllTracksDetailed()
        {
            return LoadAllTracks<WebMusicTrackDetailed>();
        }

        public WebMusicTrackBasic GetTrackBasicById(string trackId)
        {
            return LoadAllTracks<WebMusicTrackBasic>().Where(x => x.Id == trackId).First();
        }

        public WebMusicTrackDetailed GetTrackDetailedById(string trackId)
        {
            return LoadAllTracks<WebMusicTrackDetailed>().Where(x => x.Id == trackId).First();
        }

        public IEnumerable<WebMusicAlbumBasic> GetAllAlbums()
        {
            SQLFieldMapping.ReadValue singleArtistIdReader = delegate(SQLiteDataReader reader, int index)
            {
                var list = (List<string>)ArtistIdReader(reader, index);
                if (list.Count > 0)
                {
                    return list.First();
                }
                return null;
            };

            string sql = "SELECT DISTINCT t.strAlbumArtist AS albumArtist, t.strAlbum AS album, " +
                            "GROUP_CONCAT(t.strArtist, '|') AS artists, GROUP_CONCAT(t.strGenre, '|') AS genre, GROUP_CONCAT(t.strComposer, '|') AS composer, " +
                            "MIN(dateAdded) AS date, MIN(iYear) AS year " +
                         "FROM tracks t " +
                         "GROUP BY strAlbum, strAlbumArtist ";
            return new LazyQuery<WebMusicAlbumBasic>(this, sql, new List<SQLFieldMapping>()
            {
                new SQLFieldMapping("album", "Id", AlbumIdReader),
                new SQLFieldMapping("album", "Title", DataReaders.ReadString),
                new SQLFieldMapping("albumArtist", "AlbumArtist", DataReaders.ReadPipeListAsString),
                new SQLFieldMapping("albumArtist", "AlbumArtistId", singleArtistIdReader),
                new SQLFieldMapping("artists", "Artists", DataReaders.ReadPipeList),
                new SQLFieldMapping("artists", "ArtistsId", ArtistIdReader),
                new SQLFieldMapping("genre", "Genres", DataReaders.ReadPipeList),
                new SQLFieldMapping("composer", "Composer", DataReaders.ReadPipeList),
                new SQLFieldMapping("date", "DateAdded", DataReaders.ReadDateTime),
                new SQLFieldMapping("year", "Year", DataReaders.ReadInt32)
            });
        }

        public WebMusicAlbumBasic GetAlbumBasicById(string albumId)
        {
            return (GetAllAlbums() as LazyQuery<WebMusicAlbumBasic>).Where(x => x.Id == albumId).First();
        }

        public IEnumerable<WebMusicArtistBasic> GetAllArtists()
        {
            string sql = "SELECT DISTINCT strArtist FROM tracks " +
                         "UNION " +
                         "SELECT DISTINCT stralbumArtist FROM tracks ";
            return ReadList<IEnumerable<string>>(sql, delegate(SQLiteDataReader reader)
            {
                return reader.ReadPipeList(0);
            })
                .SelectMany(x => x)
                .Distinct()
                .OrderBy(x => x)
                .Select(x => new WebMusicArtistBasic()
                {
                    Id = GenerateHash(x),
                    Title = x,
                    UserDefinedCategories = new List<string>()
                }).ToList();
        }

        public WebMusicArtistBasic GetArtistBasicById(string artistId)
        {
            return GetAllArtists().Where(x => x.Id == artistId).First();
        }

        public IEnumerable<WebSearchResult> Search(string text)
        {
            return new List<WebSearchResult>();
        }

        public IEnumerable<WebGenre> GetAllGenres()
        {
            string sql = "SELECT DISTINCT strGenre FROM tracks";
            return ReadList<IEnumerable<string>>(sql, delegate(SQLiteDataReader reader)
            {
                return reader.ReadPipeList(0);
            })
                    .SelectMany(x => x)
                    .Distinct()
                    .OrderBy(x => x)
                    .Select(x => new WebGenre() { Name = x });
        }

        public IEnumerable<WebCategory> GetAllCategories()
        {
            return new List<WebCategory>();
        }

        public WebFileInfo GetFileInfo(string path)
        {
            return new WebFileInfo(new FileInfo(path));
        }

        public Stream GetFile(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read);
        }

        private string GenerateHash(string text)
        {
            if (md5 == null)
                md5 = new MD5CryptoServiceProvider();

            byte[] data = md5.ComputeHash(Encoding.UTF8.GetBytes(text));
            return Convert.ToBase64String(data).ToLower().Substring(0, 10);
        }

        private object AlbumIdReader(SQLiteDataReader reader, int index)
        {
            // make sure you always select strAlbumArtist, strAlbum when using this method
            string text = reader.ReadString(index) + "_MPExtended_" + reader.ReadString(index - 1);
            return GenerateHash(text);
        }

        private object ArtistIdReader(SQLiteDataReader reader, int index)
        {
            var artists = (List<string>)DataReaders.ReadPipeList(reader, index);
            return artists.Select(x => GenerateHash(x)).ToList();
        }
    }
}

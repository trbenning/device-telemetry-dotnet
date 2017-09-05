﻿// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.Diagnostics;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.Exceptions;
using Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services.Runtime;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Microsoft.Azure.IoTSolutions.DeviceTelemetry.Services
{
    public interface IStorageClient
    {
        DocumentClient GetDocumentClient();

        Task<ResourceResponse<DocumentCollection>> CreateCollectionIfNotExistsAsync(
            string databaseName,
            string id);

        Task<Document> UpsertDocumentAsync(
            string databaseName,
            string colId,
            Object document);

        Task<Document> DeleteDocumentAsync(
             string databaseName,
             string colId,
             string docId);

        List<Document> QueryDocuments(
             string databaseName,
             string colId,
             FeedOptions queryOptions,
             string queryString,
             int skip,
             int limit);

        Tuple<bool, string> Ping();
    }
    public class StorageClient : IStorageClient
    {
        private readonly ILogger log;
        private Uri storageUri;
        private string storagePrimaryKey;
        private int storageThroughput;
        private DocumentClient client;

        public StorageClient(
            IServicesConfig config,
            ILogger logger)
        {
            this.storageUri = config.DocumentDbUri;
            this.storagePrimaryKey = config.DocumentDbKey;
            this.storageThroughput = config.DocumentDbThroughput;
            this.log = logger;
            this.client = GetDocumentClient();
        }

        public async Task<ResourceResponse<DocumentCollection>> CreateCollectionIfNotExistsAsync(
            string databaseName,
            string id)
        {
            DocumentCollection collectionInfo = new DocumentCollection();
            RangeIndex index = Index.Range(DataType.String, -1);
            collectionInfo.IndexingPolicy = new IndexingPolicy(
                new Index[] { index });
            collectionInfo.Id = id;

            // Azure Cosmos DB collections can be reserved with
            // throughput specified in request units/second.
            RequestOptions requestOptions = new RequestOptions();
            requestOptions.OfferThroughput = storageThroughput;
            string dbUrl = "/dbs/" + databaseName;
            string colUrl = dbUrl + "/colls/" + id;
            bool create = false;
            ResourceResponse<DocumentCollection> response = null;

            try
            {
                response = await this.client.ReadDocumentCollectionAsync(
                    colUrl,
                    requestOptions);
            }
            catch (DocumentClientException dcx)
            {
                if (dcx.StatusCode == HttpStatusCode.NotFound)
                {
                    create = true;
                }
                else
                {
                    log.Error("Error reading collection.",
                        () => new { id, dcx });
                }
            }

            if (create)
            {
                try
                {
                    response = await this.client.CreateDocumentCollectionAsync(
                        dbUrl,
                        collectionInfo,
                        requestOptions);
                }
                catch (Exception ex)
                {
                    log.Error("Error creating collection.",
                        () => new { id, dbUrl, collectionInfo, ex });
                    throw ex;
                }
            }

            return response;
        }

        public async Task<Document> DeleteDocumentAsync(
            string databaseName,
            string colId,
            string docId)
        {
            string docUrl = String.Format(
                "/dbs/{0}/colls/{1}/docs/{2}",
                databaseName,
                colId,
                docId);

            try
            {
                return await this.client.DeleteDocumentAsync(
                    docUrl,
                    new RequestOptions());
            }
            catch (Exception ex)
            {
                this.log.Error("Error deleting document in collection",
                    () => new { colId, ex });
                throw ex;
            }
        }

        public DocumentClient GetDocumentClient()
        {
            if (this.client == null)
            {
                try
                {
                    this.client = new DocumentClient(
                        this.storageUri,
                        this.storagePrimaryKey,
                        ConnectionPolicy.Default,
                        ConsistencyLevel.Session);
                }
                catch (Exception e)
                {
                    this.log.Error("Could not connect to DocumentClient, " +
                        "check connection string",
                        () => new { this.storageUri, e });
                    throw new InvalidConfigurationException(
                        "Could not connect to DocumentClient, " +
                        "check connection string");
                }

                if (this.client == null)
                {
                    this.log.Error("Could not connect to DocumentClient",
                        () => new { this.storageUri });
                    throw new InvalidConfigurationException(
                        "Could not connect to DocumentClient");
                }
            }

            return this.client;
        }

        public Tuple<bool, string> Ping()
        {
            Uri response = null;

            if (this.client != null)
            {
                // make generic call to see if storage client can be reached
                response = this.client.ReadEndpoint;
            }

            if (response != null)
            {
                return new Tuple<bool, string>(true, "OK: Alive and well");
            }
            else
            {
                return new Tuple<bool, string>(false,
                    "Could not reach storage service. " +
                    "Check connection string");
            }
        }

        public List<Document> QueryDocuments(
            string databaseName,
            string colId,
            FeedOptions queryOptions,
            string queryString,
            int skip,
            int limit)
        {
            if (queryOptions == null)
            {
                queryOptions = new FeedOptions();
                queryOptions.EnableCrossPartitionQuery = true;
                queryOptions.EnableScanInQuery = true;
            }

            List<Document> docs = new List<Document>();
            string collectionLink = string.Format(
                "/dbs/{0}/colls/{1}",
                databaseName,
                colId);

            var queryResults = this.client.CreateDocumentQuery<Document>(
                    collectionLink,
                    queryString,
                    queryOptions)
                .AsEnumerable()
                .Skip(skip )
                .Take(limit);

            foreach (Document doc in queryResults)
            {
                docs.Add(doc);
            }

            this.log.Info("Query results count:", () => new { docs.Count });

            return docs;
        }

        public async Task<Document> UpsertDocumentAsync(
            string databaseName,
            string colId,
            object document)
        {
            string colUrl = string.Format("/dbs/{0}/colls/{1}",
                databaseName, colId);

            try
            {
                return await this.client.UpsertDocumentAsync(colUrl, document,
                    new RequestOptions(), false);
            }
            catch (Exception ex)
            {
                log.Error("Error upserting document.", (() => new { colId, ex }));
                throw ex;
            }
        }
    }
}

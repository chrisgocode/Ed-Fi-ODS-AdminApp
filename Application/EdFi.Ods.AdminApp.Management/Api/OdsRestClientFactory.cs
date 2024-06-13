// SPDX-License-Identifier: Apache-2.0
// Licensed to the Ed-Fi Alliance under one or more agreements.
// The Ed-Fi Alliance licenses this file to you under the Apache License, Version 2.0.
// See the LICENSE and NOTICES files in the project root for more information.

using System.Text.Json;
using System.Threading.Tasks;
using RestSharp;
using RestSharp.Serializers.Json;

namespace EdFi.Ods.AdminApp.Management.Api
{
    public class OdsRestClientFactory : IOdsRestClientFactory
    {
        private readonly IOdsApiConnectionInformationProvider _odsApiConnectionInformationProvider;
        private readonly IOdsApiValidator _odsApiValidator;
        private TokenRetriever _tokenRetriever;
        private RestClient _restClient;

        public OdsRestClientFactory(IOdsApiConnectionInformationProvider odsApiConnectionInformationProvider, IOdsApiValidator odsApiValidator)
        {
            _odsApiConnectionInformationProvider = odsApiConnectionInformationProvider;
            _odsApiValidator = odsApiValidator;
        }

        public async Task<IOdsRestClient> Create()
        {
            var connectionInfo = await _odsApiConnectionInformationProvider.GetConnectionInformationForEnvironment();
            var validatorResult = await _odsApiValidator.Validate(connectionInfo.ApiServerUrl);

            if (!validatorResult.IsValidOdsApi)
            {
                throw validatorResult.Exception;
            }

            _tokenRetriever = new TokenRetriever(connectionInfo);
            var restOptions = new RestClientOptions(connectionInfo.ApiBaseUrl)
            {
#if DEBUG
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true,
#endif
            };
            _restClient = new RestClient(
                restOptions,
                configureSerialization: s =>
                    s.UseSystemTextJson(new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                    })
                );
            return new OdsRestClient(connectionInfo, _restClient, _tokenRetriever);
        }
    }
}

﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IdentityModel.Tokens;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using TokenClient.Common;
using TokenClient.Common.Tokens;
using TokenClient.Protocols.OAuth2.Http;

namespace TokenClient.Protocols.OAuth2
{
    public abstract class AuthorizationCodeFlowBase
    {
        protected string _accessCode;
        private readonly string _state;
        protected readonly AuthorizationCodeTokenRequest _tokenRequest;
        protected readonly Uri _serviceUri;
        protected readonly IHttpClient _httpClient;
        private WebToken _cachedToken;

        public AuthorizationCodeFlowBase(Uri serviceUri, AuthorizationCodeTokenRequest tokenRequest)
            : this(serviceUri, tokenRequest, new OAuthHttpClient())
        {

        }

        public AuthorizationCodeFlowBase(Uri serviceUri, AuthorizationCodeTokenRequest tokenRequest, IHttpClient httpClient)
        {
            _serviceUri = serviceUri;
            _tokenRequest = tokenRequest;
            _httpClient = httpClient;
            _state = Guid.NewGuid().ToString("N");
        }

        public string FlowId 
        { 
            get { return _state; } 
        }

        public Uri GetAuthorizationUri()
        {
            Dictionary<string, string> parameters = GetAuthorizationRequestParameters();

            var urlParts = new UrlParts(AuthorizationEndpoint, parameters);
            Uri uri = urlParts.BuildUri();
            
            return uri;
        }

        protected virtual Uri AuthorizationEndpoint
        {
            get { return new Uri(_serviceUri, "authorize"); }
        }

        protected virtual Dictionary<string,string> GetAuthorizationRequestParameters()
        {
            var parameters = new Dictionary<string, string>(5);
            parameters.Add("response_type", "code");
            parameters.Add("client_id", _tokenRequest.ClientId);
            parameters.Add("redirect_uri", _tokenRequest.RedirectUri.ToString());
            parameters.Add("state", FlowId);
            parameters.Add("scope", _tokenRequest.Scope);

            return parameters;
        }

        public void SetAccessCodeRepsonse(Uri resultUrl)
        {
            var urlParts = new UrlParts(resultUrl);

            string state = GetStateValueFromParameters(urlParts.QueryParameters);
            if (!string.IsNullOrEmpty(state))
            {
                VerifyStateParameter(state);
            }

            string code = GetAuthorizationCodeFromParameters(urlParts.QueryParameters);

            if (string.IsNullOrEmpty(code))
            {
                throw new ArgumentException("No access code found");
            }

            _accessCode = code;
        }

        protected virtual string GetAuthorizationCodeFromParameters(Dictionary<string,string> parameters)
        {
            return parameters["code"];
        }

        protected virtual string GetStateValueFromParameters(Dictionary<string, string> parameters)
        {
            if (parameters.ContainsKey("state"))
            {
                return parameters["state"];
            }
            else
            {
                return null;
            }
        }

        protected virtual void VerifyStateParameter(string state)
        {
            if (!state.Equals(FlowId, StringComparison.InvariantCultureIgnoreCase))
            {
                throw new ArgumentException("Response does not belong to this flow.");
            }
        }

        public string GetAccessTokenAsString()
        {
            string tokenString = RequestAccessToken();
            return tokenString;
        }

        private string RequestAccessToken()
        {
            if (_cachedToken == null || _cachedToken.Expiration < DateTime.Now.AddMinutes(1))
            {
                Dictionary<string, string> parameters = CreateAccessTokenRequestParameters();
                ProtocolRequest oauthRequest = CreateProtocolRequest(parameters);
                ProtocolResponse oauthResponse = _httpClient.SendRequest(oauthRequest);

                _cachedToken = ExtractSecurityTokenFromResponse(oauthResponse);
            }

            return _cachedToken.Token;
        }

        public SecurityToken GetAccessToken()
        {
            string tokenString = RequestAccessToken();
            var token = new JwtSecurityToken(tokenString);

            return token;
        }

        protected virtual Dictionary<string, string> CreateAccessTokenRequestParameters()
        {
            var formParameters = new Dictionary<string, string>()
            {
                {"grant_type", "authorization_code"},
                {"client_id", _tokenRequest.ClientId},
                {"client_secret", _tokenRequest.ClientSecret},
                {"redirect_uri", _tokenRequest.RedirectUri.ToString()},
                {"code", _accessCode}
            };

            return formParameters;
        }

        protected virtual Uri TokenRequestEndpoint
        {
            get { return new Uri(_serviceUri, "token"); }
        }

        protected virtual WebToken ExtractSecurityTokenFromResponse(ProtocolResponse oauthResponse)
        {
            string tokenType = oauthResponse.BodyParameters["token_type"];
            string accessTokenString = oauthResponse.BodyParameters["access_token"];
            string lifetime = oauthResponse.BodyParameters["expires_in"];

            var token = new WebToken(accessTokenString);
            token.Expiration.AddSeconds(int.Parse(lifetime));
            return token;
        }

        protected virtual ProtocolRequest CreateProtocolRequest(Dictionary<string, string> parameters)
        {
            var oauthRequest = new ProtocolRequest()
            {
                BodyParameters = parameters,
                Url = new UrlParts(TokenRequestEndpoint)
            };

            return oauthRequest;
        }
    }
}

﻿using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Arcus.Security.Secrets.Core.Interfaces;
using Arcus.WebApi.Security.Authentication.Certificates;
using Arcus.WebApi.Unit.Hosting;
using Arcus.WebApi.Unit.Security.Doubles;
using Xunit;
using static Arcus.WebApi.Unit.Security.Authentication.CertificateAuthenticationOnMethodController;

namespace Arcus.WebApi.Unit.Security.Authentication
{
    public class CertificateAuthenticationAttributeTests : IDisposable
    {
        public const string SubjectKey = "subject", 
                            IssuerKey = "isser", 
                            ThumbprintKey = "thumbprint";

        private readonly TestApiServer _testServer = new TestApiServer();

        [Theory]
        [InlineData("known-subject")]
        [InlineData("unknown-subject")]
        public async Task AuthorizedRoute_WithCertificateAuthenticationOnAppServiceHeader_ShouldSucceeds_WhenClientCertificateSubjectNameMatches(string actualSubject)
        {
            // Arrange
            _testServer.AddService<ISecretProvider>(new InMemorySecretProvider((SubjectKey, $"CN={actualSubject}")));
            _testServer.AddService(
                new CertificateAuthenticationValidator(
                    new CertificateAuthenticationConfigBuilder()
                        .WithSubject(X509ValidationLocation.SecretProvider, SubjectKey)
                        .Build()));

            const string expectedSubject = "known-subject";
            using (HttpClient client = _testServer.CreateClient())
            using (var cert = SelfSignedCertificate.CreateWithSubject(expectedSubject))
            {
                var request = new HttpRequestMessage(HttpMethod.Get, AuthorizedRoute);
                string clientCertificate = Convert.ToBase64String(cert.RawData);
                request.Headers.Add("X-ARR-ClientCert", clientCertificate);

                // Act
                using (HttpResponseMessage response = await client.SendAsync(request))
                {
                    // Arrange
                    bool equalSubject = expectedSubject == actualSubject;
                    bool isUnauthorized = response.StatusCode == HttpStatusCode.Unauthorized;
                    Assert.True(equalSubject != isUnauthorized, "Client certificate with the same subject name should result in an OK HTTP status code");
                }
            }
        }

        [Fact]
        public async Task AuthorizedRoute_WithCertificateAuthentication_ShouldFailWithUnauthorized_WhenClientCertificateSubjectNameDoesntMatch()
        {
            // Arrange
            _testServer.AddService<ISecretProvider>(new InMemorySecretProvider((SubjectKey, "CN=subject")));
            _testServer.AddService(
                new CertificateAuthenticationValidator(
                        new CertificateAuthenticationConfigBuilder()
                            .WithSubject(X509ValidationLocation.SecretProvider, SubjectKey)
                            .Build()));

            _testServer.SetClientCertificate(SelfSignedCertificate.CreateWithSubject("unrecognized-subject-name"));
            using (HttpClient client = _testServer.CreateClient())
            {
                var request = new HttpRequestMessage(HttpMethod.Get, AuthorizedRoute);

                // Act
                using (HttpResponseMessage response = await client.SendAsync(request))
                {
                    // Arrange
                    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                }
            }
        }

        [Theory]
        [InlineData("subject", "issuer", "", false)]
        [InlineData("subject", "issuer", "unrecognized-thumbprint", true)]
        [InlineData("subject", "unrecognized-issuer", "", true)]
        [InlineData("subject", "unrecognized-issuer", "unrecognized-thumbprint", true)]
        [InlineData("unrecognized-subject", "issuer", "", true)]
        [InlineData("unrecognized-subject", "issuer", "unrecognized-thumbprint", true)]
        [InlineData("unrecognized-subject", "unrecognized-issuer", "", true)]
        [InlineData("unrecognized-subject", "unrecognized-issuer", "unrecognized-thumbprint", true)]
        public async Task AuthorizedRoute_WithCertificateAuthenticationViaSecretProvider_ShouldFailWithUnauthorized_WhenAnyClientCertificateValidationDoesntSucceeds(
            string subjectName,
            string issuerName,
            string thumbprintNoise,
            bool expected)
        {
            // Arrange
            using (X509Certificate2 clientCertificate = SelfSignedCertificate.CreateWithIssuerAndSubjectName(issuerName, subjectName))
            {
                _testServer.AddService<ISecretProvider>(
                    new InMemorySecretProvider(
                        (SubjectKey, "CN=subject"),
                        (IssuerKey, "CN=issuer"),
                        (ThumbprintKey, clientCertificate.Thumbprint + thumbprintNoise)));

                _testServer.AddService(
                    new CertificateAuthenticationValidator(
                            new CertificateAuthenticationConfigBuilder()
                                .WithSubject(X509ValidationLocation.SecretProvider, SubjectKey)
                                .WithIssuer(X509ValidationLocation.SecretProvider, IssuerKey)
                                .WithThumbprint(X509ValidationLocation.SecretProvider, ThumbprintKey)
                                .Build()));

                _testServer.SetClientCertificate(clientCertificate);
                using (HttpClient client = _testServer.CreateClient())
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, AuthorizedRoute);

                    // Act
                    using (HttpResponseMessage response = await client.SendAsync(request))
                    {
                        // Assert
                        Assert.True(
                            (HttpStatusCode.Unauthorized == response.StatusCode) == expected,
                            $"Response HTTP status code {(expected ? "should" : "shouldn't")} be 'Unauthorized' but was '{response.StatusCode}'");
                    }
                }
            }
        }

        [Theory]
        [InlineData("subject", "issuer", "", false)]
        [InlineData("subject", "issuer", "unrecognized-thumbprint", true)]
        [InlineData("subject", "unrecognized-issuer", "", true)]
        [InlineData("subject", "unrecognized-issuer", "unrecognized-thumbprint", true)]
        [InlineData("unrecognized-subject", "issuer", "", true)]
        [InlineData("unrecognized-subject", "issuer", "unrecognized-thumbprint", true)]
        [InlineData("unrecognized-subject", "unrecognized-issuer", "", true)]
        [InlineData("unrecognized-subject", "unrecognized-issuer", "unrecognized-thumbprint", true)]
        public async Task AuthorizedRoute_WithCertificateAuthenticationViaConfiguration_ShouldFailWithUnauthorized_WhenAnyClientCertificateValidationDoesntSucceeds(
            string subjectName,
            string issuerName,
            string thumbprintNoise,
            bool expected)
        {
            // Arrange
            using (X509Certificate2 clientCertificate = SelfSignedCertificate.CreateWithIssuerAndSubjectName(issuerName, subjectName))
            {
                _testServer.AddConfigKeyValue(SubjectKey, "CN=subject");
                _testServer.AddConfigKeyValue(IssuerKey, "CN=issuer");
                _testServer.AddConfigKeyValue(ThumbprintKey, clientCertificate.Thumbprint + thumbprintNoise);

                _testServer.AddService(
                    new CertificateAuthenticationValidator(
                            new CertificateAuthenticationConfigBuilder()
                                .WithSubject(X509ValidationLocation.Configuration, SubjectKey)
                                .WithIssuer(X509ValidationLocation.Configuration, IssuerKey)
                                .WithThumbprint(X509ValidationLocation.Configuration, ThumbprintKey)
                                .Build()));

                _testServer.SetClientCertificate(clientCertificate);
                using (HttpClient client = _testServer.CreateClient())
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, AuthorizedRoute);

                    // Act
                    using (HttpResponseMessage response = await client.SendAsync(request))
                    {
                        // Assert
                        Assert.True(
                            (HttpStatusCode.Unauthorized == response.StatusCode) == expected,
                            $"Response HTTP status code {(expected ? "should" : "shouldn't")} be 'Unauthorized' but was '{response.StatusCode}'");
                    }
                }
            }
        }

        [Theory]
        [InlineData("subject", "issuer", "", false)]
        [InlineData("subject", "issuer", "unrecognized-thumbprint", true)]
        [InlineData("subject", "unrecognized-issuer", "", true)]
        [InlineData("subject", "unrecognized-issuer", "unrecognized-thumbprint", true)]
        [InlineData("unrecognized-subject", "issuer", "", true)]
        [InlineData("unrecognized-subject", "issuer", "unrecognized-thumbprint", true)]
        [InlineData("unrecognized-subject", "unrecognized-issuer", "", true)]
        [InlineData("unrecognized-subject", "unrecognized-issuer", "unrecognized-thumbprint", true)]
        public async Task AuthorizedRoute_WithCertificateAuthenticationViaConfigurationSecretProviderAndCustom_ShouldFailWithUnauthorized_WhenAnyClientCertificateValidationDoesntSucceeds(
            string subjectName,
            string issuerName,
            string thumbprintNoise,
            bool expected)
        {
            // Arrange
            using (X509Certificate2 clientCertificate = SelfSignedCertificate.CreateWithIssuerAndSubjectName(issuerName, subjectName))
            {
                _testServer.AddConfigKeyValue(SubjectKey, "CN=subject");
                _testServer.AddService<ISecretProvider>(new InMemorySecretProvider((IssuerKey, "CN=issuer")));
                _testServer.AddService(
                    new CertificateAuthenticationValidator(
                            new CertificateAuthenticationConfigBuilder()
                                .WithSubject(X509ValidationLocation.Configuration, SubjectKey)
                                .WithIssuer(X509ValidationLocation.SecretProvider, IssuerKey)
                                .WithThumbprint(new StubX509ValidationLocation(clientCertificate.Thumbprint + thumbprintNoise), ThumbprintKey)
                                .Build()));

                _testServer.SetClientCertificate(clientCertificate);
                using (HttpClient client = _testServer.CreateClient())
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, AuthorizedRoute);

                    // Act
                    using (HttpResponseMessage response = await client.SendAsync(request))
                    {
                        // Assert
                        Assert.True(
                            (HttpStatusCode.Unauthorized == response.StatusCode) == expected,
                            $"Response HTTP status code {(expected ? "should" : "shouldn't")} be 'Unauthorized' but was '{response.StatusCode}'");
                    }
                }
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            _testServer.Dispose();
        }
    }
}

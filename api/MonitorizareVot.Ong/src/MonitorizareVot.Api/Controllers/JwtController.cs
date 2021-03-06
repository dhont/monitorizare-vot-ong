﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using MonitorizareVot.Ong.Api.Common;
using MonitorizareVot.Ong.Api.ViewModels;
using Microsoft.IdentityModel.Tokens;
using Jwt;
using System.Collections.Generic;
using MediatR;
using System.Linq;

namespace MonitorizareVot.Ong.Api.Controllers
{
    [Route("api/v1/auth")]
    public class JwtController : Controller
    {
        private readonly IMediator _mediator;
        //private static string SecretKey = "needtogetthisfromenvironment";
        //private static SymmetricSecurityKey _signingKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(SecretKey));
        private readonly JwtIssuerOptions _jwtOptions;

        private readonly ILogger _logger;
        private readonly JsonSerializerSettings _serializerSettings;

        public JwtController(IOptions<JwtIssuerOptions> jwtOptions, ILoggerFactory loggerFactory, IMediator mediator)
        {
            _mediator = mediator;
            _jwtOptions = jwtOptions.Value;
            ThrowIfInvalidOptions(_jwtOptions);

            _logger = loggerFactory.CreateLogger<JwtController>();

            _serializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };
        }


        [HttpGet]
        [AllowAnonymous]
        // this method will only be called the token is expired
        public async Task<IActionResult> RefreshLogin()
        {
            string token = Request.Headers["Authorization"];
            if (string.IsNullOrEmpty(token))
            {
                return Forbid();
            }
            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = token.Substring("Bearer ".Length).Trim();
            }
            if (string.IsNullOrEmpty(token))
            {
                return Forbid();
            }

            var decoded = JsonWebToken.DecodeToObject<Dictionary<string, string>>(token,
                _jwtOptions.SigningCredentials.Kid, false);
            var idOng = Int32.Parse(decoded["IdOng"]);
            var organizator = bool.Parse(decoded["Organizator"]);
            var userName = decoded[JwtRegisteredClaimNames.Sub];

            var json = await generateToken(userName, idOng, organizator);

            return new OkObjectResult(json);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] ApplicationUser applicationUser)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var identity = await GetClaimsIdentity(applicationUser);
            if (identity == null)
            {
                _logger.LogInformation(
                    $"Invalid username ({applicationUser.UserName}) or password ({applicationUser.Password})");
                return BadRequest("Invalid credentials");
            }
            var json = await generateToken(applicationUser.UserName, int.Parse(identity.Claims.FirstOrDefault(c => c.Type == "IdOng")?.Value),
                 bool.Parse(identity.Claims.FirstOrDefault(c => c.Type == "Organizator")?.Value));

            return new OkObjectResult(json);
        }


        [Authorize]
        [HttpPost("test")]
        public async Task<object> Test()
        {
            var claims = User.Claims.Select(c => new
            {
                Type = c.Type,
                Value = c.Value
            });

            return await Task.FromResult(claims);
        }

        private async Task<string> generateToken(string userName, int idOng = 0, bool organizator = false)
        {
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userName),
                new Claim(JwtRegisteredClaimNames.Jti, await _jwtOptions.JtiGenerator()),
                new Claim(JwtRegisteredClaimNames.Iat,
                    ToUnixEpochDate(_jwtOptions.IssuedAt).ToString(),
                    ClaimValueTypes.Integer64),
                new Claim("IdOng", idOng.ToString()),
                new Claim("Organizator", organizator.ToString())
            };

            // Create the JWT security token and encode it.
            var jwt = new JwtSecurityToken(
                issuer: _jwtOptions.Issuer,
                audience: _jwtOptions.Audience,
                claims: claims,
                notBefore: _jwtOptions.NotBefore,
                expires: _jwtOptions.Expiration,
                signingCredentials: _jwtOptions.SigningCredentials);

            var encodedJwt = new JwtSecurityTokenHandler().WriteToken(jwt);

            // Serialize and return the response
            //var response = new
            //{
            //    token = encodedJwt,
            //    expires_in = (int)_jwtOptions.ValidFor.TotalSeconds
            //};

            //var json = JsonConvert.SerializeObject(response, _serializerSettings);
            return encodedJwt;
        }


        private static void ThrowIfInvalidOptions(JwtIssuerOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            if (options.ValidFor <= TimeSpan.Zero)
            {
                throw new ArgumentException("Must be a non-zero TimeSpan.", nameof(JwtIssuerOptions.ValidFor));
            }

            if (options.SigningCredentials == null)
            {
                throw new ArgumentNullException(nameof(JwtIssuerOptions.SigningCredentials));
            }

            if (options.JtiGenerator == null)
            {
                throw new ArgumentNullException(nameof(JwtIssuerOptions.JtiGenerator));
            }
        }

        /// <returns>Date converted to seconds since Unix epoch (Jan 1, 1970, midnight UTC).</returns>
        private static long ToUnixEpochDate(DateTime date)
            => (long)Math.Round((date.ToUniversalTime() -
                                  new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero))
                .TotalSeconds);

        private async Task<ClaimsIdentity> GetClaimsIdentity(ApplicationUser user)
        {
            var userInfo = await _mediator.Send(user);

            if (userInfo == null)
                return await Task.FromResult<ClaimsIdentity>(null);

            return await Task.FromResult(new ClaimsIdentity(
                new GenericIdentity(user.UserName, "Token"), new[]
                {
                    new Claim("IdOng", userInfo.IdOng.ToString()),
                    new Claim("Organizator", userInfo.Organizator.ToString(), typeof(bool).ToString())
                }));
        }

    }
}


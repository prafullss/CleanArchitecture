﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Identity.Api.EndpointBases;
using Identity.Application.Infrastrucures;
using Identity.Application.Services;
using Identity.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Swashbuckle.AspNetCore.Annotations;
using TanvirArjel.ArgumentChecker;

namespace Identity.Api.Endpoints.UserEndpoints
{
    [ApiVersion("1.0")]
    public class UserLoginEndpoint : UserEndpoint
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IApplicationUserService _applicationUserService;
        private readonly ITokenGenerator _tokenGenerator;
        private readonly IConfiguration _configuration;
        private readonly IExceptionLogger _exceptionLogger;

        public UserLoginEndpoint(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IApplicationUserService applicationUserService,
            IConfiguration configuration,
            IExceptionLogger exceptionLogger,
            ITokenGenerator tokenGenerator)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _applicationUserService = applicationUserService;
            _configuration = configuration;
            _exceptionLogger = exceptionLogger;
            _tokenGenerator = tokenGenerator;
        }

        [AllowAnonymous]
        [HttpPost("login")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [ProducesDefaultResponseType]
        [SwaggerOperation(Summary = "Post the required credentials to get the access token for the login.")]
        public async Task<ActionResult<LoginResponseModel>> Post(LoginModel loginModel)
        {
            try
            {
                ApplicationUser applicationUser = await _userManager.FindByEmailAsync(loginModel.EmailOrUserName);

                if (applicationUser == null)
                {
                    ModelState.AddModelError(nameof(loginModel.EmailOrUserName), "The email does not exist.");
                    return BadRequest(ModelState);
                }

                Microsoft.AspNetCore.Identity.SignInResult signinResult = await _signInManager.PasswordSignInAsync(
                         loginModel.EmailOrUserName,
                         loginModel.Password,
                         isPersistent: loginModel.RememberMe,
                         lockoutOnFailure: false);

                if (signinResult.Succeeded)
                {
                    LoginResponseModel jsonWebToken = await GenerateLoginResponse(applicationUser);
                    return Ok(jsonWebToken);
                }

                if (signinResult.IsNotAllowed)
                {
                    if (!await _userManager.IsEmailConfirmedAsync(applicationUser))
                    {
                        ModelState.AddModelError(nameof(loginModel.EmailOrUserName), "The email is not confirmed yet.");
                        return BadRequest(ModelState);
                    }

                    if (!await _userManager.IsPhoneNumberConfirmedAsync(applicationUser))
                    {
                        ModelState.AddModelError(string.Empty, "The phone number is not confirmed yet.");
                        return BadRequest(ModelState);
                    }
                }
                else if (signinResult.IsLockedOut)
                {
                    ModelState.AddModelError(string.Empty, "The account is locked.");
                    return BadRequest(ModelState);
                }
                else if (signinResult.RequiresTwoFactor)
                {
                    ModelState.AddModelError(string.Empty, "Require two factor authentication.");
                    return BadRequest(ModelState);
                }
                else
                {
                    ModelState.AddModelError(nameof(loginModel.Password), "Password is incorrect.");
                    return BadRequest(ModelState);
                }

                return BadRequest(ModelState);
            }
            catch (Exception exception)
            {
                loginModel.Password = null;
                await _exceptionLogger.LogAsync(exception, loginModel);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        private async Task<LoginResponseModel> GenerateLoginResponse(ApplicationUser applicationUser)
        {
            applicationUser.ThrowIfNull(nameof(applicationUser));

            IList<string> roles = await _userManager.GetRolesAsync(applicationUser).ConfigureAwait(false);

            string accessToken = _tokenGenerator.GenerateJwtToken(applicationUser, roles);

            RefreshToken refreshToken = await _applicationUserService.GetRefreshTokenAsync(applicationUser.Id);

            if (refreshToken == null)
            {
                string token = _tokenGenerator.GenerateRefreshToken();
                refreshToken = await _applicationUserService.StoreRefreshTokenAsync(applicationUser.Id, token);
            }
            else
            {
                if (refreshToken.ExpireAtUtc < DateTime.UtcNow)
                {
                    string token = _tokenGenerator.GenerateRefreshToken();
                    refreshToken = await _applicationUserService.UpdateRefreshTokenAsync(applicationUser.Id, token);
                }
            }

            int tokenLifeTime = _configuration.GetValue<int>("Jwt:Lifetime"); // Seconds

            LoginResponseModel loginResponse = new LoginResponseModel()
            {
                UserId = applicationUser.Id,
                FullName = applicationUser.FullName,
                UserName = applicationUser.UserName,
                Email = applicationUser.Email,
                AccessToken = accessToken,
                AccessTokenExpireAtUtc = DateTime.UtcNow.AddSeconds(tokenLifeTime),
                RefreshToken = refreshToken.Token,
                RefreshTokenExpireAtUtc = refreshToken.ExpireAtUtc,
            };

            return loginResponse;
        }
    }

    public class LoginModel
    {
        [Required]
        [EmailAddress]
        [StringLength(50, MinimumLength = 5, ErrorMessage = "{0} should be between {2} to {1} characters")]
        public string EmailOrUserName { get; set; }

        [Required]
        [StringLength(20, MinimumLength = 6, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.")]
        public string Password { get; set; }

        public bool RememberMe { get; set; }
    }

    public class LoginResponseModel
    {
        public Guid UserId { get; set; }

        public string FullName { get; set; }

        public string UserName { get; set; }

        public string Email { get; set; }

        public string AccessToken { get; set; }

        public DateTime AccessTokenExpireAtUtc { get; set; }

        public string RefreshToken { get; set; }

        public DateTime RefreshTokenExpireAtUtc { get; set; }
    }
}

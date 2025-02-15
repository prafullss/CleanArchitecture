﻿using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MauiBlazor.Shared.Common;
using MauiBlazor.Shared.Models.IdentityModels;
using MauiBlazor.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using TanvirArjel.Blazor;
using TanvirArjel.Blazor.Components;
using TanvirArjel.Blazor.Extensions;

namespace MauiBlazor.WebUI.Components.IdentityComponents
{
    public partial class LoginComponent
    {
        private readonly UserService _userService;
        private readonly HostAuthStateProvider _hostAuthStateProvider;
        private readonly ExceptionLogger _exceptionLogger;
        private readonly NavigationManager _navigationManager;

        public LoginComponent(
            UserService userService,
            HostAuthStateProvider hostAuthStateProvider,
            ExceptionLogger exceptionLogger,
            NavigationManager navigationManager)
        {
            _userService = userService;
            _hostAuthStateProvider = hostAuthStateProvider;
            _exceptionLogger = exceptionLogger;
            _navigationManager = navigationManager;
        }

        private EditContext FormContext { get; set; }

        private LoginModel LoginModel { get; set; } = new LoginModel();

        private CustomValidationMessages ValidationMessages { get; set; }

        private bool IsSubmitDisabled { get; set; }

        private string ErrorMessage { get; set; }

        protected override void OnInitialized()
        {
            FormContext = new EditContext(LoginModel);
            FormContext.SetFieldCssClassProvider(new BootstrapValidationClassProvider());
        }

        protected override void OnAfterRender(bool firstRender)
        {
            if (firstRender)
            {
                string error = _navigationManager.GetQuery("error");

                if (!string.IsNullOrWhiteSpace(error))
                {
                    ValidationMessages.AddAndDisplay(string.Empty, error);
                }
            }
        }

        private async Task HandleValidSubmit()
        {
            try
            {
                IsSubmitDisabled = true;
                HttpResponseMessage httpResponse = await _userService.LoginAsync(LoginModel);

                if (httpResponse.IsSuccessStatusCode)
                {
                    JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions()
                    {
                        PropertyNameCaseInsensitive = true
                    };

                    string responseString = await httpResponse.Content.ReadAsStringAsync();
                    LoggedInUserInfo loginResponse = JsonSerializer.Deserialize<LoggedInUserInfo>(responseString, jsonSerializerOptions);

                    if (loginResponse != null)
                    {
                        await _hostAuthStateProvider.LogInAsync(loginResponse, "/");
                        return;
                    }

                    Console.WriteLine("Called");
                }
                else
                {
                    Console.WriteLine((int)httpResponse.StatusCode);
                    await ValidationMessages.AddAndDisplayAsync(httpResponse);
                }
            }
            catch (HttpRequestException httpException)
            {
                Console.WriteLine($"Status Code: {httpException.StatusCode}");
                ValidationMessages.AddAndDisplay(AppErrorMessage.ServerErrorMessage);
                await _exceptionLogger.LogAsync(httpException);
            }
            catch (Exception exception)
            {
                ValidationMessages.AddAndDisplay(AppErrorMessage.ClientErrorMessage);
                await _exceptionLogger.LogAsync(exception);
            }

            IsSubmitDisabled = false;
        }

        private void LoginWithGoogle()
        {
            string loginUrl = "https://localhost:44363/api/v1/external-login/sign-in?provider=Google";
            _navigationManager.NavigateTo(loginUrl, true);
        }
    }
}

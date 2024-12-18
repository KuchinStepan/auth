using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using PhotosApp.Areas.Identity.Data;
using PhotosApp.Services;
using PhotosApp.Services.Authorization;
using PhotosApp.Services.TicketStores;

[assembly: HostingStartup(typeof(PhotosApp.Areas.Identity.IdentityHostingStartup))]
namespace PhotosApp.Areas.Identity
{
    public class IdentityHostingStartup : IHostingStartup
    {
        public void Configure(IWebHostBuilder builder)
        {
            builder.ConfigureServices((context, services) => {
                services.AddDbContext<UsersDbContext>(options =>
                    options.UseSqlite(
                        context.Configuration.GetConnectionString("UsersDbContextConnection")));
                
                services.AddDbContext<TicketsDbContext>(options =>
                    options.UseSqlite(
                        context.Configuration.GetConnectionString("TicketsDbContextConnection")));

                services.AddDefaultIdentity<PhotosAppUser>()
                    .AddRoles<IdentityRole>()
                    .AddClaimsPrincipalFactory<CustomClaimsPrincipalFactory>()
                    .AddErrorDescriber<RussianIdentityErrorDescriber>()
                    .AddPasswordValidator<UsernameAsPasswordValidator<PhotosAppUser>>()
                    .AddEntityFrameworkStores<UsersDbContext>();
                
                services.ConfigureExternalCookie(options =>
                {
                    options.Cookie.Name = "PhotosApp.Auth.External";
                    options.Cookie.HttpOnly = true;
                    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                    options.SlidingExpiration = true;
                });
                
                services.AddAuthentication()
                    //.AddGoogle("Google", options =>
                    //{
                    //    options.ClientId = context.Configuration["Authentication:Google:ClientId"];
                    //    options.ClientSecret = context.Configuration["Authentication:Google:ClientSecret"];
                    //})
                    .AddOpenIdConnect(
                        authenticationScheme: "Google",
                        displayName: "Google",
                        options =>
                        {
                            // options.SaveTokens = true;
                            options.Authority = "https://accounts.google.com/";
                            options.ClientId = context.Configuration["Authentication:Google:ClientId"];
                            options.ClientSecret = context.Configuration["Authentication:Google:ClientSecret"];

                            options.CallbackPath = "/signin-google";
                            options.SignedOutCallbackPath = "/signout-callback-google";
                            options.RemoteSignOutPath = "/signout-google";

                            options.Scope.Add("email");
                        });
                
                services.Configure<IdentityOptions>(options =>
                {
                    options.Password.RequireDigit = false;
                    options.Password.RequireLowercase = true;
                    options.Password.RequireNonAlphanumeric = false;
                    options.Password.RequireUppercase = false;
                    options.SignIn.RequireConfirmedAccount = false;
                });
                
                services.AddTransient<IEmailSender, SimpleEmailSender>(serviceProvider =>
                    new SimpleEmailSender(
                        serviceProvider.GetRequiredService<ILogger<SimpleEmailSender>>(),
                        serviceProvider.GetRequiredService<IWebHostEnvironment>(),
                        context.Configuration["SimpleEmailSender:Host"],
                        context.Configuration.GetValue<int>("SimpleEmailSender:Port"),
                        context.Configuration.GetValue<bool>("SimpleEmailSender:EnableSSL"),
                        context.Configuration["SimpleEmailSender:UserName"],
                        context.Configuration["SimpleEmailSender:Password"]
                    ));
                
                services.AddAuthentication()
                    .AddJwtBearer(options =>
                    {
                        options.RequireHttpsMetadata = false;
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = false,
                            ValidateAudience = false,
                            ValidateLifetime = true,
                            ClockSkew = TimeSpan.Zero,
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = TemporaryTokens.SigningKey,
                        };
                        options.Events = new JwtBearerEvents
                        {
                            OnMessageReceived = c =>
                            {
                                c.Token = c.Request.Cookies["TemporaryToken"];
                                return Task.CompletedTask;
                            }
                        };
                    });
                
                services.AddTransient<EntityTicketStore>();
                services.AddScoped<IAuthorizationHandler, MustOwnPhotoHandler>();
                services.ConfigureApplicationCookie(options =>
                {
                    var serviceProvider = services.BuildServiceProvider();
                    //options.SessionStore = serviceProvider.GetRequiredService<EntityTicketStore>();
                    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
                    options.Cookie.Name = "PhotosApp.Auth";
                    options.Cookie.HttpOnly = true;
                    options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
                    options.LoginPath = "/Identity/Account/Login";
                    options.ReturnUrlParameter = CookieAuthenticationDefaults.ReturnUrlParameter;
                    options.SlidingExpiration = true;
                });
                
                services.AddAuthorization(options =>
                {
                    options.DefaultPolicy = new AuthorizationPolicyBuilder(
                            JwtBearerDefaults.AuthenticationScheme,
                            IdentityConstants.ApplicationScheme)
                        .RequireAuthenticatedUser()
                        .Build();
                    options.AddPolicy(
                        "Beta",
                        policyBuilder =>
                        {
                            policyBuilder.RequireAuthenticatedUser();
                            policyBuilder.RequireClaim("testing", "beta");
                        });
                    options.AddPolicy(
                        "CanAddPhoto",
                        policyBuilder =>
                        {
                            policyBuilder.RequireAuthenticatedUser();
                            policyBuilder.RequireClaim("subscription", "paid");
                        });
                    options.AddPolicy(
                        "Dev",
                        policyBuilder =>
                        {
                            policyBuilder.RequireAuthenticatedUser();
                            //policyBuilder.RequireRole("Dev");
                            //policyBuilder.AddAuthenticationSchemes(
                            //    JwtBearerDefaults.AuthenticationScheme,
                            //    IdentityConstants.ApplicationScheme);
                        });
                    options.AddPolicy(
                        "MustOwnPhoto",
                        policyBuilder =>
                        {
                            policyBuilder.RequireAuthenticatedUser();
                            policyBuilder.AddRequirements(new MustOwnPhotoRequirement());
                        });
                });
            });
        }
    }
}
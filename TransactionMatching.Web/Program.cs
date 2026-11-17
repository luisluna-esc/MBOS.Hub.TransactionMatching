using System.Net;
using System.Text.Json.Serialization;
using TransactionMatching.Core.Entities.ResponseApi.Details;
using TransactionMatching.Core.Entities.ResponseApi.DisplayFormat;
using TransactionMatching.Infrastructure.DependencyInjection;
using TransactionMatching.Web.BackgroundServices;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Solo consola y depuración. En Windows, ASP.NET agrega además el Registro de eventos, que pide
// permisos de administrador para crear su origen: si un servicio en segundo plano registraba un
// error (ej. la purga de auditoría al cerrarse el backend), ese registro fallaba y tumbaba el proceso.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container.
builder.Services.AddControllers()
    // MessageType y demás enums viajan como texto ("Success", "Error") en lugar de 0, 1, 2.
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var entries = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToList();

            var hasRootError = entries.Any(entry => string.IsNullOrWhiteSpace(entry.Key));
            var pathErrors = entries.Where(entry => entry.Key.StartsWith("$.")).ToList();

            Message[] messages;
            if (hasRootError)
            {
                messages = [new Message { Type = MessageType.Error, Description = "The request body is invalid or missing." }];
            }
            else if (pathErrors.Count > 0)
            {
                messages = pathErrors
                    .Select(entry => new Message
                    {
                        Type = MessageType.Error,
                        Description = $"The field '{entry.Key.TrimStart('$', '.')}' has an invalid or missing value."
                    })
                    .ToArray();
            }
            else
            {
                messages = entries
                    .Select(entry => new Message
                    {
                        Type = MessageType.Error,
                        Description = $"The field '{entry.Key}' has an invalid or missing value."
                    })
                    .ToArray();
            }

            var response = new ResponsePost
            {
                Id = 0,
                Messages = messages,
                StatusCode = HttpStatusCode.BadRequest
            };

            return new BadRequestObjectResult(response);
        };
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter the access token returned by /api/auth/login."
    });

    // Marks every operation as requiring the "Bearer" scheme so the Swagger UI "Authorize"
    // button actually attaches the token to requests (AddSecurityDefinition alone only
    // documents the scheme, it does not apply it to any operation).
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document, null)] = []
    });
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<AuditRetentionBackgroundService>();

var jwtSection = builder.Configuration.GetSection("Jwt");
// La clave no va en appsettings.json: en desarrollo sale de user-secrets y en el servidor del .env
// (Jwt__Key). Sin ella el backend no arranca, en vez de firmar tokens con una clave vacía.
var jwtKey = jwtSection["Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
    throw new InvalidOperationException("Missing 'Jwt:Key'. Set it with user-secrets (development) or the Jwt__Key environment variable.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

// Sin catálogo de permisos: los endpoints piden sesión iniciada ([Authorize]) y los de
// administración además el rol M-BOS ([Authorize(Roles = RoleNames.Admin)]).
builder.Services.AddAuthorization();

const string CorsPolicy = "CorsPolicy";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        policy.SetIsOriginAllowed(origin =>
              {
                  // localhost (ng serve) solo se acepta en desarrollo; en producción, solo la lista.
                  var host = new Uri(origin).Host;
                  return (builder.Environment.IsDevelopment() && host is "localhost" or "127.0.0.1")
                      || allowedOrigins.Contains(origin);
              })
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

app.UseCors(CorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

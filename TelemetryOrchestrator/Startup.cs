using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TelemetryOrchestrator.Entities;
using TelemetryOrchestrator.Hubs;
using TelemetryOrchestrator.Interfaces;
using TelemetryOrchestrator.Services;
using TelemetryOrchestrator.Services.Http_Requests;

namespace TelemetryOrchestrator
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<AutoScalerSettings>(Configuration.GetSection(nameof(AutoScalerSettings)));
            services.AddSingleton(resolver => resolver.GetRequiredService<IOptions<AutoScalerSettings>>().Value);

            services.AddSignalR();
            services.AddSingleton<IRegistryManager, RegistryManager>();
            services.AddSingleton<LoadMonitorService>();
            services.AddSingleton<HttpService>();

            services.AddHostedService(provider => provider.GetRequiredService<LoadMonitorService>()); 
            
            services.AddHttpClient<HttpService>();

            
            services.AddCors(options =>
            {
                options.AddPolicy("AllowAngularApp", builder =>
                {
                    builder.WithOrigins("http://localhost:4200")
                           .AllowAnyMethod()
                           .AllowAnyHeader()
                           .AllowCredentials();
                });
            });
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "TelemetryOrchestrator", Version = "v1" });
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "TelemetryOrchestrator v1"));
            }

            app.UseRouting();

            app.UseCors("AllowAngularApp");

            app.UseWebSockets();
            
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHub<LiveHub>("/livehub");
            });
        }
    }
}

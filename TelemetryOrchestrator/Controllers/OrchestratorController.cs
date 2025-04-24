using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TelemetryOrchestrator.Controllers
{
    public class OrchestratorController : Controller
    {
        // GET: OrchestratorController
        public ActionResult Index()
        {
            return View();
        }

        // GET: OrchestratorController/Details/5
        public ActionResult Details(int id)
        {
            return View();
        }

        // GET: OrchestratorController/Create
        public ActionResult Create()
        {
            return View();
        }

        // POST: OrchestratorController/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: OrchestratorController/Edit/5
        public ActionResult Edit(int id)
        {
            return View();
        }

        // POST: OrchestratorController/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: OrchestratorController/Delete/5
        public ActionResult Delete(int id)
        {
            return View();
        }

        // POST: OrchestratorController/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }
    }
}

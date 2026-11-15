using GLMS.Web.ApiClients;
using GLMS.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GLMS.Web.Controllers;

public class ClientsController : Controller
{
    private readonly IClientsApi _clients;

    public ClientsController(IClientsApi clients) => _clients = clients;

    public async Task<IActionResult> Index(CancellationToken ct) => View(await _clients.GetAllAsync(ct));

    public async Task<IActionResult> Details(int id, CancellationToken ct) => View(await _clients.GetAsync(id, ct));

    public IActionResult Create() => View(new ClientFormViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(ClientFormViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            var client = await _clients.CreateAsync(model.ToRequest(), ct);
            this.Flash($"Client '{client.Name}' was created.");
            return RedirectToAction(nameof(Details), new { id = client.Id });
        }
        catch (ApiValidationException ex)
        {
            ModelState.AddApiErrors(ex, typeof(ClientFormViewModel));
            return View(model);
        }
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct) =>
        View(ClientFormViewModel.From(await _clients.GetAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ClientFormViewModel model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid) return View(model);
        try
        {
            await _clients.UpdateAsync(id, model.ToRequest(), ct);
            this.Flash("Client details were updated.");
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (ApiValidationException ex)
        {
            ModelState.AddApiErrors(ex, typeof(ClientFormViewModel));
            return View(model);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        try
        {
            await _clients.DeleteAsync(id, ct);   // Admin role only (403 is shown by ApiExceptionFilter)
            this.Flash("Client was deleted.");
            return RedirectToAction(nameof(Index));
        }
        catch (ApiValidationException ex)
        {
            this.Flash(ex.AllMessages, "danger");
            return RedirectToAction(nameof(Details), new { id });
        }
    }
}

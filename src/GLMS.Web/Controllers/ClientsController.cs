using GLMS.Core.Exceptions;
using GLMS.Core.Services;
using GLMS.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GLMS.Web.Controllers;

public class ClientsController : Controller
{
    private readonly ClientService _clients;

    public ClientsController(ClientService clients) => _clients = clients;

    public async Task<IActionResult> Index(CancellationToken ct) => View(await _clients.GetAllAsync(ct));

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        try { return View(await _clients.GetAsync(id, ct)); }
        catch (NotFoundException) { return NotFound(); }
    }

    public IActionResult Create() => View(new ClientFormViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(ClientFormViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            var client = await _clients.CreateAsync(ToInput(model), ct);
            this.Flash($"Client '{client.Name}' was created.");
            return RedirectToAction(nameof(Details), new { id = client.Id });
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddBusinessErrors(ex);
            return View(model);
        }
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        try
        {
            var c = await _clients.GetAsync(id, ct);
            return View(new ClientFormViewModel
            {
                Id = c.Id, Name = c.Name, ContactPerson = c.ContactPerson, Email = c.Email, PhoneNumber = c.PhoneNumber, Region = c.Region
            });
        }
        catch (NotFoundException) { return NotFound(); }
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ClientFormViewModel model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid) return View(model);
        try
        {
            await _clients.UpdateAsync(id, ToInput(model), ct);
            this.Flash("Client details were updated.");
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (NotFoundException) { return NotFound(); }
        catch (BusinessRuleException ex)
        {
            ModelState.AddBusinessErrors(ex);
            return View(model);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        try
        {
            await _clients.DeleteAsync(id, ct);
            this.Flash("Client was deleted.");
            return RedirectToAction(nameof(Index));
        }
        catch (NotFoundException) { return NotFound(); }
        catch (BusinessRuleException ex)
        {
            this.Flash(ex.Message, "danger");
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    private static ClientInput ToInput(ClientFormViewModel m) =>
        new(m.Name, m.ContactPerson ?? string.Empty, m.Email, m.PhoneNumber ?? string.Empty, m.Region);
}

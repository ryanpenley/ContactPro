using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ContactPro.Data;
using ContactPro.Models;
using ContactPro.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using ContactPro.Services.Interfaces;
using ContactPro.Models.ViewModels;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace ContactPro.Controllers
{
    [Authorize]
    public class ContactsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly IImageService _imageService;
        private readonly IContactProService _contactProService;
        private readonly IEmailSender _emailService;

        public ContactsController(ApplicationDbContext context,
                              UserManager<AppUser> userManager,
                                    IImageService imageService,
                                    IContactProService contactProService,
                                    IEmailSender emailService)
        {
            _context = context;
            _userManager = userManager;
            _imageService = imageService;
            _contactProService = contactProService;
            _emailService = emailService;
        }

        // GET: Contacts

        public async Task<IActionResult> Index(int? categoryId ,string? swalMessage = null)
        {
            ViewData["SwalMessage"] = swalMessage;
            
            string userId = _userManager.GetUserId(User)!;

            // Gets the Contacts from the AppUser
            List<Contact> contacts = new List<Contact>();


            // Gets the categories from the Appuser based on whether they have chosen a Category to "filter" by
            List<Category> categories = await _context.Categories
                                                      .Where(c => c.AppUserId == userId)
                                                      .ToListAsync();

            if (categoryId == null)
            {
                contacts = await _context.Contacts
                                            .Where(c => c.AppUserId == userId)
                                            .Include(c => c.Categories)
                                            .ToListAsync();
            }
            else
            {
                contacts = (await _context.Categories
                                         .Include(c => c.Contacts)
                                         .FirstOrDefaultAsync(c => c.AppUserId == userId && c.Id == categoryId))!
                                         .Contacts.ToList();
            }



            ViewData["CategoryId"] = new SelectList(categories, "Id","Name", categoryId);

            return View(contacts);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SearchContacts(string? searchString)
        {
            string? userId = _userManager.GetUserId(User)!;

            // Gets the Contacts from the AppUser
            List<Contact> contacts = new List<Contact>();


            AppUser? appUser = await _context.Users
                                             .Include(u => u.Contacts)
                                                .ThenInclude(c => c.Categories)
                                              .FirstOrDefaultAsync(u => u.Id == userId);

            if (string.IsNullOrEmpty(searchString))
            {
                contacts = appUser!.Contacts
                                   .OrderBy(c => c.LastName)
                                   .ThenBy(c => c.FirstName)
                                   .ToList();
            }
            else
            {
                contacts = appUser!.Contacts
                                   .Where(c => c.FullName!.ToLower().Contains(searchString.ToLower()))
                                   .OrderBy(c => c.LastName)
                                   .ThenBy(c => c.FirstName)
                                   .ToList();
            }

            ViewData["CategoryId"] = new SelectList(appUser.Categories, "Id", "Name");

            return View(nameof(Index), contacts);
        }

        // EmailContact
        public async Task<IActionResult> EmailContact(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            string? appUserId = _userManager.GetUserId(User)!;

            Contact? contact = await _context.Contacts
                                           .FirstOrDefaultAsync(c => c.Id == id && c.AppUserId == appUserId);

            if(contact == null)
            {
                return NotFound();
            }


            // Instaniate EmailData

            EmailData emailData = new EmailData()
            {
                EmailAddress = contact!.Email,
                FirstName = contact.FirstName,
                LastName = contact.LastName
            };

            // Instaniate the ViewModel

            EmailContactViewModel viewModel = new EmailContactViewModel()
            {
                Contact = contact,
                EmailData = emailData
            };


            return View(viewModel);
        }


        // POST: Email Contact
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmailContact(EmailContactViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                string? swalMessage = string.Empty;

                try
                {
                    await _emailService.SendEmailAsync(viewModel.EmailData!.EmailAddress!, viewModel.EmailData.EmailSubject!, viewModel.EmailData.EmailBody!);

                    swalMessage = "Success: Your Email has been sent!";

                    return RedirectToAction(nameof(Index),new {swalMessage});
                }
                catch (Exception)
                {
                    swalMessage = "Error! Your Email Failed to Send!";
                    return RedirectToAction(nameof(Index),new {swalMessage});
                    throw;
                }
            }

            return View(viewModel);
        }


        // GET: Contacts/Details/5

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || _context.Contacts == null)
            {
                return NotFound();
            }

            var contact = await _context.Contacts
                .Include(c => c.AppUser)
                .FirstOrDefaultAsync(m => m.Id == id);


            if (contact == null)
            {
                return NotFound();
            }

            return View(contact);
        }

        // GET: Contacts/Create

        public async Task<IActionResult> Create()
        {

            // Query and present the list of categories for the logged in user
            string? userId = _userManager.GetUserId(User);

            IEnumerable<Category> categoriesList = await _context.Categories
                                                                 .Where(c => c.AppUserId == userId)
                                                                 .ToListAsync();

            ViewData["CategoryList"] = new MultiSelectList(categoriesList, "Id", "Name");



            ViewData["StatesList"] = new SelectList(Enum.GetValues(typeof(States)).Cast<States>());
            return View();
        }

        // POST: Contacts/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]

        public async Task<IActionResult> Create([Bind("Id,AppUserId,FirstName,LastName,BirthDate,Address1,Address2,City,State,ZipCode,Email,PhoneNumber,Created,ImageFile")] Contact contact, IEnumerable<int> selected)
        {

            ModelState.Remove("AppUserId");

            if (ModelState.IsValid)
            {
                contact.AppUserId = _userManager.GetUserId(User);
                contact.Created = DateTime.UtcNow;


                // adding and converting image
                if (contact.ImageFile != null)
                {
                    contact.ImageData = await _imageService.ConvertFileToByteArrayAsync(contact.ImageFile);
                    contact.ImageType = contact.ImageFile.ContentType;
                }



                if (contact.BirthDate != null)
                {
                    contact.BirthDate = DateTime.SpecifyKind(contact.BirthDate.Value, DateTimeKind.Utc);
                }

                _context.Add(contact);
                await _context.SaveChangesAsync();


                // loops over selected categoryIds to find the category entities in the database
                foreach (int catergoryId in selected)
                {
                    Category? category = await _context.Categories.FindAsync(catergoryId);

                    category!.Contacts.Add(contact);
                }
                // saves category changes to the database
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }
            ViewData["StatesList"] = new SelectList(Enum.GetValues(typeof(States)));
            return View(contact);
        }

        // GET: Contacts/Edit/5

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.Contacts == null)
            {
                return NotFound();
            }

            ViewData["StatesList"] = new SelectList(Enum.GetValues(typeof(States)).Cast<States>());

            var contact = await _context.Contacts
                                        .Include(c => c.Categories)
                                        .FirstOrDefaultAsync(c => c.Id == id);


            // query and present the list of categories for the logged in user
            string? userId = _userManager.GetUserId(User);

            IEnumerable<Category> categoriesList = await _context.Categories
                                                                 .Where(c => c.AppUserId == userId)
                                                                 .ToListAsync();


            IEnumerable<int> currentCategories = contact!.Categories.Select(c => c.Id);


            ViewData["CategoryList"] = new MultiSelectList(categoriesList, "Id", "Name", currentCategories);



            if (contact == null)
            {
                return NotFound();
            }


            ViewData["AppUserId"] = new SelectList(_context.Users, "Id", "Id", contact.AppUserId);
            return View(contact);
        }

        // POST: Contacts/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]

        public async Task<IActionResult> Edit(int id, [Bind("Id,AppUserId,FirstName,LastName,BirthDate,Address1,Address2,City,State,ZipCode,Email,PhoneNumber,ImageData,ImageType,Created")] Contact contact, IEnumerable<int> selected)
        {
            if (id != contact.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Reformat Created Date 
                    contact.Created = DateTime.SpecifyKind(contact.Created, DateTimeKind.Utc);

                    // Reformat if Image was Updated
                    if (contact.ImageFile != null)
                    {
                        contact.ImageData = await _imageService.ConvertFileToByteArrayAsync(contact.ImageFile);
                        contact.ImageType = contact.ImageFile.ContentType;
                    }


                    // Reformat Birth Date
                    if (contact.BirthDate != null)
                    {
                        contact.BirthDate = DateTime.SpecifyKind(contact.BirthDate.Value, DateTimeKind.Utc);
                    }

                    _context.Update(contact);
                    await _context.SaveChangesAsync();



                    // Added ContactProService

                    if (selected != null)
                    {
                        // 1. Remove Contact's categories
                        await _contactProService.RemoveAllContactCategoriesAsync(contact.Id);

                        // 2. Add selected categories to the contact
                        await _contactProService.AddContactToCategoriesAsync(selected, contact.Id);

                    }

                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ContactExists(contact.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["AppUserId"] = new SelectList(_context.Users, "Id", "Id", contact.AppUserId);
            return View(contact);
        }

        // GET: Contacts/Delete/5

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null || _context.Contacts == null)
            {
                return NotFound();
            }

            var contact = await _context.Contacts
                .Include(c => c.AppUser)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (contact == null)
            {
                return NotFound();
            }

            return View(contact);
        }

        // POST: Contacts/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]

        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (_context.Contacts == null)
            {
                return Problem("Entity set 'ApplicationDbContext.Contacts'  is null.");
            }
            var contact = await _context.Contacts.FindAsync(id);
            if (contact != null)
            {
                _context.Contacts.Remove(contact);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool ContactExists(int id)
        {
            return (_context.Contacts?.Any(e => e.Id == id)).GetValueOrDefault();
        }
    }
}

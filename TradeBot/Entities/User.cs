using System;
using System.Collections.Generic;

namespace TradeBot.Entities;

public partial class User
{
    public int Id { get; set; }

    public long ChatId { get; set; }

    public string? FullName { get; set; }

    public string? PhoneNumber { get; set; }

    public bool? IsPermium { get; set; }

    public bool? IsActive { get; set; }

    public bool? IsAdmin { get; set; }
}

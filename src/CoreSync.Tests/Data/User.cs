﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace CoreSync.Tests.Data
{
    public class User
    {
        [Key]
        public required string Email { get; set; }

        public string? Name { get; set; }

        [Column("Date Created(date/$time)")]
        public DateTime Created { get; set; }

        public List<Post> Posts { get; set; } = [];

        public List<Comment> Comments { get; set; } = [];
    }
}

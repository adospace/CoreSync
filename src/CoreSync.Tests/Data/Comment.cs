using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace CoreSync.Tests.Data
{
    public class Comment
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Post? Post { get; set; }

        public User? Author { get; set; }

        public string? Content { get; set; }

        public DateTime Created { get; set; }

        public Comment? ReplyTo { get; set; }
    }
}

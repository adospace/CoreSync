using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace CoreSync.Tests.Data
{
    public class Post
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Title { get; set; }

        public string Content { get; set; }

        public User Author { get; set; }

        public DateTime Updated { get; set; }

        public int Claps { get; set; }

        public float Stars { get; set; }

        public List<Comment> Comments { get; set; } = new List<Comment>();

    }
}

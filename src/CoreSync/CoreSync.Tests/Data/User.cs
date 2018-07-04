using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace CoreSync.Tests.Data
{
    public class User
    {
        [Key]
        public string Email { get; set; }

        public string Name { get; set; }

        public DateTime Created { get; set; }

        public List<Post> Posts { get; set; } = new List<Post>();

        public List<Comment> Comments { get; set; } = new List<Comment>();
    }
}

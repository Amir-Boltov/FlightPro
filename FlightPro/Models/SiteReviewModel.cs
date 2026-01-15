using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Azure.Cosmos;

namespace FlightPro.Models
{
    public class SiteReviewModel
    {
        public int Id { get; set; }
        public string UserName { get; set; }
        public string Comment { get; set; }
        public int Rating { get; set; } // 1-5
           public DateTime Date { get; set; }
        
    }
}
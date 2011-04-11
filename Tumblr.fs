// Copyright (c) 2011, Kevin Cantu <me@kevincantu.org>
//
// Permission to use, copy, modify, and/or distribute this software for any
// purpose with or without fee is hereby granted, provided that the above
// copyright notice and this permission notice appear in all copies.
//
// The software is provided "as is" and the author disclaims all warranties
// with regard to this software including all implied warranties of
// merchantability and fitness.  In no event shall the author be liable for
// any special, direct, indirect, or consequential damanges or any damages
// whatsoever resulting from loss of use, data, your immortal soul, or 
// profits, whether in an action of contract, negligence, or other 
// tortious action, arising out of or in connection with the use or 
// performance of this software.

module Ninlil.Tumblr

open System.Collections.Generic
open System.Net
open System.IO
open System.Threading
open System.Text.RegularExpressions
open System.Xml
open System.Drawing

(* 
   Ninlil.Tumblr can be used to simplify calls to the Tumblr API
   for a given account, like so:

   let api = Tumblr.API(blog, email, password)
   api.reblog POST-ID POST-REBLOGKEY
   api.delete POST-ID
   api.read INDEX
   api.reads (INDEX,COUNT)

   Also, remember that until Tumblr starts using HTTPS,
   this could all be dangerously insecure.
*)


// HTTP utility functions /////////////////////////////////////

// combines key/values into a string with = and &
// for use with HTTP GET and POST
let combine (m: Map<string,string>) : string = 
   Map.fold (fun state key v -> 
                  let next = key + "=" + v
                  match state with
                  | "" ->               next
                  | _  -> state + "&" + next) 
            ""
            m


// HTTP GET
// Note: the point of the async {} is to try not to block so much,
// but in this program it is over-engineering. :D
let httpget (url: string) (data: Map<string,string> ) : string = 
   Async.RunSynchronously(async {
      let url' = url + "?" + (combine data)

      let req        = WebRequest.Create(url', Timeout=15000)
      use! response  = req.AsyncGetResponse()
      use reader     = new StreamReader(response.GetResponseStream())
      let output = reader.ReadToEnd()
      return output
   })


// HTTP POST
let httppost (url: string) (data: Map<string,string>) : string =
   Async.RunSynchronously(async {
      let data' : byte[] = System.Text.Encoding.ASCII.GetBytes(combine data);

      let request = WebRequest.Create(url, Timeout=15000)  // sensitive to too short a delay
      request.Method        <- "POST"
      request.ContentType   <- "application/x-www-form-urlencoded"
      request.ContentLength <- (int64) data'.Length

      use wstream = request.GetRequestStream() 
      wstream.Write(data',0, (data'.Length))
      wstream.Flush()
      wstream.Close()

      use! response = request.AsyncGetResponse()
      use reader    = new StreamReader(response.GetResponseStream())
      let output    = reader.ReadToEnd()

      reader.Close()
      response.Close()
      request.Abort()

      return output
   })


// Tumblr /////////////////////////////////////

// one Tumblr post
// This could be expanded to include more of the properties present
type Post(postxml: XmlNode) =

   // "2010-11-24 05:57:26 GMT"
   let processDate (datestring: string) : System.DateTime =
      (System.DateTime.ParseExact( (datestring.Split [| ' ' |]).[0], "yyyy-MM-dd", null )) 

   member p.XML : XmlNode           = postxml
   member p.id : string             = postxml.Attributes.GetNamedItem("id").Value
   member p.rkey : string           = postxml.Attributes.GetNamedItem("reblog-key").Value
   member p.date : System.DateTime  = postxml.Attributes.GetNamedItem("date-gmt").Value |> processDate
   member p.picURL : string         = postxml.ChildNodes.Item(1).InnerText

   member p.display : string        = 
      sprintf "id: '%s', rkey: '%s', '%s'\n   %s" p.id p.rkey (p.date.ToString()) p.picURL


// API for a given account
type API(blog: string, email: string, password: string) =

   // read via personal Tumblr API
   member public x.readXML (start: int) (num: int) : string = 
         let url  = "http://" + blog + ".tumblr.com/api/read"
         let data = Map.ofList [ "start", (sprintf "%d" start);
                                 "num",   (sprintf "%d" num);
                                 "type",  "photo" ]

         printfn "-> reading..."
         let xml = httpget url data
         xml


   // delete using Tumblr API
   member public x.delete (id: string) : string =
         let url  = "http://www.tumblr.com/api/delete"
         let data = Map.ofList [ "email",    email;
                                 "password", password;
                                 "post-id",  id ]

         printfn "-# deleting..."
         let status = httppost url data
         printfn "   status: '%s'" status

         status
                        

   // reblog using Tumblr API
   // often works even though Tumblr returns an error
   member public x.reblog (id: string) (rkey: string) : string =
         let url  = "http://www.tumblr.com/api/reblog"
         let data = Map.ofList [ "email",      email; 
                                 "password",   password; 
                                 "post-id",    id; 
                                 "reblog-key", rkey ]

         printfn "-* reblogging id='%s' rkey='%s'..." id rkey
         let newid = httppost url data
         printfn "   newid: '%s'" newid

         newid


   // process XML for Posts
   member private x.processPosts (postsXML) =
      let doc = XmlDocument()
      postsXML |> doc.LoadXml // so doc is mutable?

      // add prettier printing
      //fsi.AddPrinter( fun (x:XmlNode) -> x.OuterXml );;

      // process the data
      let tumblr = doc.ChildNodes.Item(1)
      let posts  = tumblr.ChildNodes.Item(1)
      let num   = posts.ChildNodes.Count

      // posts.HasChildNodes
      let postsFound = [ for kk in 0..(num-1) do
                         let postxml = posts.ChildNodes.Item(kk)
                         yield Post(postxml) ]

      // display a post tuple
      let display (post: Post) = 
         printfn "   %s" (post.display) |> ignore

      // print stats
      let start = System.Convert.ToInt32(posts.Attributes.GetNamedItem("start").Value)
      printfn "   read %d to %d" start (start+num-1) |> ignore

      // print all
      List.map display postsFound |> ignore

      postsFound


   // request a post, then find the total posts
   member public x.totalPosts() =
      let doc = XmlDocument()
      x.readXML 0 1 |> doc.LoadXml

      // process the data
      let tumblr = doc.ChildNodes.Item(1)
      let posts  = tumblr.ChildNodes.Item(1)
      let total = System.Convert.ToInt32(posts.Attributes.GetNamedItem("total").Value)
      total


   // processed read
   member public x.reads index count = x.readXML index count |> x.processPosts
   member public x.read  index       = x.reads index 1       |> List.head


